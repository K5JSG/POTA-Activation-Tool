using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PotaActivationTool
{
    // Request handling for the park map's loopback-only HTTP server (see
    // Form1.StartMapServer for why the map is served rather than opened as
    // a file). Routes:
    //   /gps                 - current GPS mode/fix as JSON (Form1's GPS dropdown)
    //   /assets/...          - Leaflet, protomaps-leaflet and the offline map
    //                          style, built into the exe (MapAssets/ in the
    //                          project) so the map opens with no internet
    //   /offline-map/info    - whether the loaded state's offline map is
    //                          downloaded, as JSON
    //   /offline-map.pmtiles - that map file, served in byte ranges (the
    //                          map reads only the parts it needs to draw)
    //   /save-map?plan=id    - a saved copy of the map page to download
    //                          (Layers panel > Save a copy of this map)
    //   /rove/plans          - the rove planner's saved plans (GET list)
    //   /rove/plans/{id}     - one plan: GET, PUT (save) or DELETE
    //   /rove/route?c=...    - driving route through the stops (RoveService)
    //   /rove/pota/plans     - your rove plans on pota.app (PotaAccountService)
    //   /rove/pota/plans/{n} - one of them, in the planner's own format
    //   anything else        - the map page itself
    public static class MapServer
    {
        public const string AssetPrefix = "/assets/";

        // Embedded resource name -> resource, keyed by the path under
        // MapAssets/ with forward slashes. The .csproj names resources after
        // their path, but MSBuild's RecursiveDir keeps Windows backslashes
        // for subfolders, so both are normalized here.
        private static readonly Lazy<Dictionary<string, string>> AssetResourceNames = new(() =>
            typeof(MapServer).Assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith("MapAssets/", StringComparison.Ordinal))
                .ToDictionary(n => n.Substring("MapAssets/".Length).Replace('\\', '/'), n => n, StringComparer.OrdinalIgnoreCase));

        private static readonly Regex RangeHeader = new(@"^bytes=(\d*)-(\d*)$", RegexOptions.Compiled);

        // Reads one of the built-in map assets as raw bytes.
        public static byte[] ReadAssetBytes(string path)
        {
            if (!AssetResourceNames.Value.TryGetValue(path, out string? name)) return Array.Empty<byte>();
            using var stream = typeof(MapServer).Assembly.GetManifestResourceStream(name);
            if (stream == null) return Array.Empty<byte>();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        // Reads one of the built-in map assets as text (the map page inlines
        // the offline style this way - see MapService.BuildMapHtml).
        public static string ReadAssetText(string path)
        {
            if (!AssetResourceNames.Value.TryGetValue(path, out string? name)) return "";
            using var stream = typeof(MapServer).Assembly.GetManifestResourceStream(name);
            if (stream == null) return "";
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        // Runs until listener.Stop() makes GetContext() throw. Each request
        // is handled on the thread pool: the offline map fires off many
        // small range reads at once while drawing, and serving them one at a
        // time would leave the map filling in noticeably slower.
        public static void Run(HttpListener listener, string html, Func<string> getGpsJson, Func<OfflineMapInfo> getOfflineMap, string appDataFolder)
        {
            byte[] htmlBody = Encoding.UTF8.GetBytes(html);
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = listener.GetContext();
                }
                catch
                {
                    return;
                }
                ThreadPool.QueueUserWorkItem(_ => Handle(context, htmlBody, getGpsJson, getOfflineMap, appDataFolder));
            }
        }

        public readonly record struct OfflineMapInfo(string StateCode, string? FilePath);

        private static void Handle(HttpListenerContext context, byte[] htmlBody, Func<string> getGpsJson, Func<OfflineMapInfo> getOfflineMap, string appDataFolder)
        {
            var response = context.Response;
            try
            {
                string path = context.Request.Url?.AbsolutePath ?? "/";
                if (path == "/save-map")
                {
                    // Built from the page as served, with the rove plans and
                    // their cached routes baked in, and where to ask for the
                    // GPS receiver's position - see MapService.ToStandaloneHtml.
                    string html = Encoding.UTF8.GetString(htmlBody);
                    string embed = RoveService.BuildEmbedJson(appDataFolder, context.Request.QueryString["plan"] ?? "");
                    string gpsUrl = $"http://{context.Request.Url?.Authority}/gps?key={GetGpsKey(appDataFolder)}";
                    string gpsEmbed = JsonSerializer.Serialize(new { url = gpsUrl });
                    string state = getOfflineMap().StateCode;
                    response.ContentType = "text/html; charset=utf-8";
                    response.Headers["Content-Disposition"] = $"attachment; filename=\"POTA_{(state.Length > 0 ? state : "Parks")}_Map.html\"";
                    response.Headers["Cache-Control"] = "no-store";
                    Write(response, Encoding.UTF8.GetBytes(MapService.ToStandaloneHtml(html, embed, gpsEmbed)));
                }
                else if (path == "/gps")
                {
                    if (!AllowSavedCopy(context, appDataFolder)) return;
                    WriteJson(response, getGpsJson());
                }
                else if (path == "/offline-map/info")
                {
                    var info = getOfflineMap();
                    WriteJson(response, JsonSerializer.Serialize(new { state = info.StateCode, available = info.FilePath != null }));
                }
                else if (path == "/offline-map.pmtiles")
                {
                    ServeFileRange(context, getOfflineMap().FilePath);
                }
                else if (path.StartsWith(AssetPrefix, StringComparison.Ordinal))
                {
                    ServeAsset(response, path.Substring(AssetPrefix.Length));
                }
                else if (path.StartsWith("/rove/", StringComparison.Ordinal))
                {
                    HandleRove(context, path, appDataFolder);
                }
                else
                {
                    response.ContentType = "text/html; charset=utf-8";
                    Write(response, htmlBody);
                }
            }
            catch
            {
                // Best-effort - a browser tab closing mid-request isn't
                // worth reporting.
            }
            finally
            {
                try { response.OutputStream.Close(); } catch { /* already gone */ }
            }
        }

        // The rove planner's plans and routes - see RoveService. Runs on a
        // thread-pool thread, so waiting on the route fetch here is fine.
        private static void HandleRove(HttpListenerContext context, string path, string appDataFolder)
        {
            var request = context.Request;
            var response = context.Response;
            try
            {
                if (path == "/rove/plans" && request.HttpMethod == "GET")
                {
                    WriteJson(response, RoveService.ListPlansJson(appDataFolder));
                    return;
                }
                if (path == "/rove/route" && (request.HttpMethod == "GET" || request.HttpMethod == "OPTIONS"))
                {
                    // Also asked by a saved copy of the map, for legs it
                    // didn't have when it was saved (e.g. from where you are
                    // now to the first stop).
                    if (!AllowSavedCopy(context, appDataFolder)) return;
                    WriteJson(response, RoveService.GetRouteJsonAsync(appDataFolder, request.QueryString["c"] ?? "").GetAwaiter().GetResult());
                    return;
                }
                if (path.StartsWith("/rove/pota/plans", StringComparison.Ordinal) && request.HttpMethod == "GET")
                {
                    HandlePotaRove(response, path, appDataFolder);
                    return;
                }
                const string planPrefix = "/rove/plans/";
                string id = path.StartsWith(planPrefix, StringComparison.Ordinal) ? path.Substring(planPrefix.Length) : "";
                if (!RoveService.IsValidPlanId(id))
                {
                    response.StatusCode = 404;
                    return;
                }
                switch (request.HttpMethod)
                {
                    case "GET":
                        string? plan = RoveService.GetPlanJson(appDataFolder, id);
                        if (plan == null) response.StatusCode = 404;
                        else WriteJson(response, plan);
                        break;
                    case "PUT":
                        // A plan is a few KB; anything far bigger isn't one.
                        if (request.ContentLength64 > 1_000_000) { response.StatusCode = 413; return; }
                        using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                            WriteJson(response, RoveService.SavePlan(appDataFolder, id, reader.ReadToEnd()));
                        break;
                    case "DELETE":
                        RoveService.DeletePlan(appDataFolder, id);
                        response.StatusCode = 204;
                        break;
                    default:
                        response.StatusCode = 405;
                        break;
                }
            }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidDataException)
            {
                response.StatusCode = 400;
            }
            catch
            {
                response.StatusCode = 500;
            }
        }

        // pota.app's rove plans. Errors come back as { error: "signin" } (401:
        // not signed in, or the sign-in expired), { error: "noaccount" } (403:
        // signed in with a login that has no POTA account) or
        // { error: "offline" } (502: pota.app couldn't be reached), for
        // rove.js to explain.
        private static void HandlePotaRove(HttpListenerResponse response, string path, string appDataFolder)
        {
            try
            {
                string json;
                if (path == "/rove/pota/plans")
                    json = PotaAccountService.GetRovePlansJsonAsync(appDataFolder).GetAwaiter().GetResult();
                else if (path.StartsWith("/rove/pota/plans/", StringComparison.Ordinal)
                         && int.TryParse(path.Substring("/rove/pota/plans/".Length), out int roveId) && roveId > 0)
                    json = PotaAccountService.GetRovePlanJsonAsync(appDataFolder, roveId).GetAwaiter().GetResult();
                else
                {
                    response.StatusCode = 404;
                    return;
                }
                WriteJson(response, json);
            }
            catch (PotaNotSignedInException)
            {
                response.StatusCode = 401;
                WriteJson(response, "{\"error\":\"signin\"}");
            }
            catch (PotaNoAccountException)
            {
                response.StatusCode = 403;
                WriteJson(response, "{\"error\":\"noaccount\"}");
            }
            catch (HttpRequestException)
            {
                response.StatusCode = 502;
                WriteJson(response, "{\"error\":\"offline\"}");
            }
            catch (TaskCanceledException)
            {
                response.StatusCode = 502;
                WriteJson(response, "{\"error\":\"offline\"}");
            }
        }

        // A page this server didn't serve - a saved copy of the map, opened
        // from a file - sends an Origin header. It may use /gps and
        // /rove/route only with the key baked into it at save time
        // (GetGpsKey); otherwise any website open in the browser could read
        // where you are. Returns false when the response is already finished:
        // refused (403), or a preflight answered (204).
        private static bool AllowSavedCopy(HttpListenerContext context, string appDataFolder)
        {
            string? origin = context.Request.Headers["Origin"];
            var response = context.Response;
            if (origin == null) return context.Request.HttpMethod != "OPTIONS" || Refuse();
            if (context.Request.QueryString["key"] != GetGpsKey(appDataFolder)) return Refuse();
            response.Headers["Access-Control-Allow-Origin"] = origin;
            // Chrome asks before a page reaches 127.0.0.1 (Private Network
            // Access) - this is that answer.
            response.Headers["Access-Control-Allow-Private-Network"] = "true";
            if (context.Request.HttpMethod != "OPTIONS") return true;
            response.StatusCode = 204;
            return false;

            bool Refuse()
            {
                response.StatusCode = 403;
                return false;
            }
        }

        // The key a saved copy of the map needs to read the GPS position and
        // look up routes from the app (see AllowSavedCopy). Made once and kept in the app data folder, so
        // copies saved earlier keep working.
        private static readonly object GpsKeyGate = new();
        private static string? _gpsKey;

        private static string GetGpsKey(string appDataFolder)
        {
            lock (GpsKeyGate)
            {
                if (_gpsKey != null) return _gpsKey;
                string path = Path.Combine(appDataFolder, "MapGpsKey.txt");
                try
                {
                    if (File.Exists(path)) _gpsKey = File.ReadAllText(path).Trim();
                }
                catch
                {
                    // unreadable - a new one below
                }
                if (string.IsNullOrEmpty(_gpsKey) || _gpsKey.Length < 32)
                {
                    _gpsKey = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                    try { File.WriteAllText(path, _gpsKey); } catch { /* used for this run only */ }
                }
                return _gpsKey;
            }
        }

        private static void Write(HttpListenerResponse response, byte[] body)
        {
            response.ContentLength64 = body.Length;
            response.OutputStream.Write(body, 0, body.Length);
        }

        private static void WriteJson(HttpListenerResponse response, string json)
        {
            response.ContentType = "application/json; charset=utf-8";
            response.Headers["Cache-Control"] = "no-store";
            Write(response, Encoding.UTF8.GetBytes(json));
        }

        private static void ServeAsset(HttpListenerResponse response, string assetPath)
        {
            if (!AssetResourceNames.Value.TryGetValue(assetPath, out string? name))
            {
                response.StatusCode = 404;
                return;
            }
            using var stream = typeof(MapServer).Assembly.GetManifestResourceStream(name)!;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            response.ContentType = Path.GetExtension(assetPath).ToLowerInvariant() switch
            {
                ".js" => "text/javascript; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".png" => "image/png",
                _ => "application/octet-stream"
            };
            Write(response, ms.ToArray());
        }

        // Serves filePath honoring a single "Range: bytes=a-b" request header
        // (206 Partial Content) - all the PMTiles reader in the map page ever
        // asks for. Opened with FileShare.ReadWrite | Delete so an Offline
        // Map update or delete in the app is never blocked by the map
        // reading the same file at that moment.
        private static void ServeFileRange(HttpListenerContext context, string? filePath)
        {
            var response = context.Response;
            if (filePath == null || !File.Exists(filePath))
            {
                response.StatusCode = 404;
                return;
            }

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long length = fs.Length;
            long start = 0, end = length - 1;

            string? range = context.Request.Headers["Range"];
            if (range != null)
            {
                var m = RangeHeader.Match(range.Trim());
                if (!m.Success)
                {
                    response.StatusCode = 416;
                    response.Headers["Content-Range"] = $"bytes */{length}";
                    return;
                }
                if (m.Groups[1].Value.Length == 0)
                {
                    // "bytes=-N": the last N bytes.
                    long suffix = long.Parse(m.Groups[2].Value);
                    start = Math.Max(0, length - suffix);
                }
                else
                {
                    start = long.Parse(m.Groups[1].Value);
                    if (m.Groups[2].Value.Length > 0) end = Math.Min(long.Parse(m.Groups[2].Value), length - 1);
                }
                if (start > end || start >= length)
                {
                    response.StatusCode = 416;
                    response.Headers["Content-Range"] = $"bytes */{length}";
                    return;
                }
                response.StatusCode = 206;
                response.Headers["Content-Range"] = $"bytes {start}-{end}/{length}";
            }

            // Lets the page's PMTiles reader notice if the file is replaced
            // by an update while the map is open.
            var modified = File.GetLastWriteTimeUtc(filePath);
            response.Headers["ETag"] = "\"" + length.ToString("x") + "-" + modified.Ticks.ToString("x") + "\"";
            response.Headers["Accept-Ranges"] = "bytes";
            response.Headers["Cache-Control"] = "no-cache";
            response.ContentType = "application/octet-stream";

            long count = end - start + 1;
            response.ContentLength64 = count;
            fs.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[(int)Math.Min(count, 1 << 16)];
            while (count > 0)
            {
                int read = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
                if (read <= 0) break;
                response.OutputStream.Write(buffer, 0, read);
                count -= read;
            }
        }
    }
}
