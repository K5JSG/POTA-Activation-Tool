using ClosedXML.Excel;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PotaActivationTool
{
    public class RawPark
    {
        public string Reference { get; set; } = "";
        public string Name { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Grid { get; set; } = "";
        public List<string> States { get; set; } = new List<string>();
        public bool Active { get; set; }
    }

    public static class PotaService
    {
        public static readonly List<UsState> UsStates = new List<UsState>
        {
            new("AL","Alabama"), new("AK","Alaska"), new("AZ","Arizona"), new("AR","Arkansas"),
            new("CA","California"), new("CO","Colorado"), new("CT","Connecticut"), new("DE","Delaware"),
            new("DC","District of Columbia"), new("FL","Florida"), new("GA","Georgia"), new("HI","Hawaii"),
            new("ID","Idaho"), new("IL","Illinois"), new("IN","Indiana"), new("IA","Iowa"),
            new("KS","Kansas"), new("KY","Kentucky"), new("LA","Louisiana"), new("ME","Maine"),
            new("MD","Maryland"), new("MA","Massachusetts"), new("MI","Michigan"), new("MN","Minnesota"),
            new("MS","Mississippi"), new("MO","Missouri"), new("MT","Montana"), new("NE","Nebraska"),
            new("NV","Nevada"), new("NH","New Hampshire"), new("NJ","New Jersey"), new("NM","New Mexico"),
            new("NY","New York"), new("NC","North Carolina"), new("ND","North Dakota"), new("OH","Ohio"),
            new("OK","Oklahoma"), new("OR","Oregon"), new("PA","Pennsylvania"), new("RI","Rhode Island"),
            new("SC","South Carolina"), new("SD","South Dakota"), new("TN","Tennessee"), new("TX","Texas"),
            new("UT","Utah"), new("VT","Vermont"), new("VA","Virginia"), new("WA","Washington"),
            new("WV","West Virginia"), new("WI","Wisconsin"), new("WY","Wyoming")
        };

        private const string AllParksUrl = "https://pota.app/all_parks_ext.csv";
        private const string ParkListCacheFileName = "AllParks.cache.csv";
        private const string ParkListCacheInfoFileName = "AllParks.cache.info.txt";

        // Downloads the full POTA park list directly from pota.app, with no
        // caching - used internally by GetAllParksAsync below.
        private static async Task<string> DownloadAllParksCsvAsync(HttpClient http)
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
            return await http.GetStringAsync(AllParksUrl, cts.Token);
        }

        private static List<RawPark> ParseAllParksCsv(string csv)
        {
            var lines = SplitCsvRecords(csv);
            var results = new List<RawPark>();

            for (int i = 1; i < lines.Count; i++)
            {
                string line = lines[i].Trim('\r', '\n');
                if (string.IsNullOrWhiteSpace(line)) continue;

                var fields = ParseCsvLine(line);
                if (fields.Count < 8) continue;

                string reference = fields[0];
                string name = fields[1];
                string active = fields[2];
                string locationDesc = fields[4];
                string latStr = fields[5];
                string lonStr = fields[6];
                string grid = fields[7];

                if (!double.TryParse(latStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)) continue;
                if (!double.TryParse(lonStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)) continue;

                results.Add(new RawPark
                {
                    Reference = reference,
                    Name = name,
                    Latitude = lat,
                    Longitude = lon,
                    Grid = grid,
                    States = locationDesc.Split(',').Select(s => s.Trim()).ToList(),
                    Active = active == "1"
                });
            }

            return results;
        }

        // Returns the POTA master park list, preferring a local cache to avoid
        // re-downloading a multi-thousand-row file on every click. Behavior:
        //   - If a cached copy exists and is younger than maxAge, use it instantly
        //     with no network call at all.
        //   - Otherwise, try to download a fresh copy and cache it.
        //   - If that download fails (offline, DNS issue, etc.) but a cache
        //     exists (even a stale one), fall back to the cache rather than
        //     failing outright.
        //   - Only if there's no cache AND no network does this throw - genuinely
        //     nothing to work with in that case.
        // statusCallback (optional) receives a short human-readable status string
        // so the caller can show it in the UI.
        public static async Task<List<RawPark>> GetAllParksAsync(
            HttpClient http, string cacheFolder, TimeSpan maxAge, Action<string>? statusCallback = null)
        {
            string cachePath = Path.Combine(cacheFolder, ParkListCacheFileName);
            string infoPath = Path.Combine(cacheFolder, ParkListCacheInfoFileName);

            if (File.Exists(cachePath) && File.Exists(infoPath))
            {
                string infoText = "";
                try { infoText = File.ReadAllText(infoPath).Trim(); } catch { }

                if (DateTime.TryParse(infoText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var cachedTime)
                    && DateTime.UtcNow - cachedTime < maxAge)
                {
                    statusCallback?.Invoke("Using cached POTA park list...");
                    try
                    {
                        string cachedCsv = await File.ReadAllTextAsync(cachePath);
                        return ParseAllParksCsv(cachedCsv);
                    }
                    catch
                    {
                        // Cache read failed unexpectedly - fall through and try a fresh download instead.
                    }
                }
            }

            try
            {
                statusCallback?.Invoke("Downloading latest park list from POTA...");
                string csv = await DownloadAllParksCsvAsync(http);

                try
                {
                    Directory.CreateDirectory(cacheFolder);
                    await File.WriteAllTextAsync(cachePath, csv, Encoding.UTF8);
                    await File.WriteAllTextAsync(infoPath, DateTime.UtcNow.ToString("o"));
                }
                catch
                {
                    // Not being able to write the cache isn't fatal - we still have
                    // the data in memory for this run.
                }

                return ParseAllParksCsv(csv);
            }
            catch
            {
                if (File.Exists(cachePath))
                {
                    statusCallback?.Invoke("Couldn't reach POTA - using last cached park list.");
                    string cachedCsv = await File.ReadAllTextAsync(cachePath);
                    return ParseAllParksCsv(cachedCsv);
                }

                // No cache and no network - nothing we can do.
                throw;
            }
        }

        public static List<ParkRecord> FilterByState(List<RawPark> allParks, string stateCode)
        {
            string target = "US-" + stateCode.ToUpperInvariant();
            var results = new List<ParkRecord>();

            foreach (var p in allParks)
            {
                if (!p.Active) continue;
                bool matches = p.States.Any(s => string.Equals(s, target, StringComparison.OrdinalIgnoreCase));
                if (!matches) continue;

                results.Add(new ParkRecord
                {
                    Reference = p.Reference,
                    Name = p.Name,
                    Latitude = p.Latitude,
                    Longitude = p.Longitude,
                    Grid = p.Grid,
                    MultiState = p.States.Count > 1
                });
            }

            return results;
        }

        // Looks up county + state for every park entirely offline, using
        // CountyLookupService (bundled Census boundary data) instead of the
        // Census geocoding API. This is the change that eliminates the long wait
        // that used to come from making one network request per park - it's now
        // pure local computation, typically finishing in well under a second even
        // for a large state. Still runs on a background thread via Task.Run so a
        // very large batch never blocks the UI, and still reports progress the
        // same way calling code already expects.
        public static Task GeocodeParksAsync(List<ParkRecord> parks, string selectedStateCode, IProgress<int> progress) =>
            GeocodeCoreAsync(parks, progress, selectedStateCode);

        public static Task GeocodeExtraParksAsync(List<ParkRecord> parks, IProgress<int> progress) =>
            GeocodeCoreAsync(parks, progress, null);

        // Shared by both overloads above. selectedStateCode is only non-null for
        // GeocodeParksAsync: that's the one that needs it, to flag a multi-state
        // park for exclusion when this particular point actually falls outside
        // the state the user asked for. GeocodeExtraParksAsync's parks (found via
        // an ADIF reference, not a state filter) never go through that check.
        private static async Task GeocodeCoreAsync(List<ParkRecord> parks, IProgress<int> progress, string? selectedStateCode)
        {
            int total = parks.Count;
            if (total == 0) return;

            await Task.Run(() =>
            {
                int done = 0;
                foreach (var park in parks)
                {
                    var (county, stateAbbrev) = CountyLookupService.FindCounty(park.Latitude, park.Longitude);
                    park.County = county;
                    park.State = stateAbbrev;
                    park.ElevationFeet = ElevationLookupService.GetElevationFeet(park.Reference);

                    if (selectedStateCode != null && park.MultiState && !string.IsNullOrEmpty(stateAbbrev) &&
                        !string.Equals(stateAbbrev, selectedStateCode, StringComparison.OrdinalIgnoreCase))
                    {
                        park.Exclude = true;
                    }

                    done++;
                    if (done % 10 == 0 || done == total)
                        progress.Report((int)(done * 100.0 / total));
                }
            });
        }

        // ---- Community activation history (used by the map) ----------------------------

        private const string ActivationsUrlBase = "https://api.pota.app/park/activations/";

        // Holds what we learned about a park's activation history from the POTA API.
        public class ActivationInfo
        {
            public int Count { get; set; }
            public string LastCallsign { get; set; } = "";
            public DateTime? LastDate { get; set; }
        }

        // Looks up ONE park's activation history from the POTA API. Returns
        // null if the lookup fails for any reason (no internet, POTA API
        // hiccup, etc.) instead of throwing, so one bad park doesn't stop the
        // whole map from being built - and so FetchActivationInfoAsync can
        // tell "couldn't check" (keep what's cached) apart from a real answer
        // of zero activations (worth caching).
        public static async Task<ActivationInfo?> GetActivationInfoAsync(HttpClient http, string reference)
        {
            var info = new ActivationInfo();
            try
            {
                // We ask for up to 250 of the most recent activations. That is far more
                // than almost every park will ever have, so Count ends up being the
                // real total for the vast majority of parks. A handful of extremely
                // popular parks could have more than 250 all-time activations, in which
                // case this number would be a "250+" style undercount.
                string url = ActivationsUrlBase + Uri.EscapeDataString(reference) + "?count=250";
                // Same "don't let one slow/hung request eat HttpClient's full
                // 100s default" reasoning as every other network call in this
                // file - this one's called once per park (up to 6 at a time,
                // see FetchActivationInfoAsync below), so a single stuck
                // request would otherwise slow the whole batch far more than
                // its already-tolerant catch-and-continue behavior intends.
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
                string json = await http.GetStringAsync(url, cts.Token);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    info.Count = doc.RootElement.GetArrayLength();
                    if (info.Count > 0)
                    {
                        var first = doc.RootElement[0];
                        info.LastCallsign = GetStringProperty(first, "activeCallsign", "callsign", "activatorCallsign") ?? "";

                        // Confirmed against real working code that calls this exact
                        // endpoint: the field is "qso_date" (snake_case), formatted
                        // as an 8-digit YYYYMMDD string - the same convention ADIF
                        // itself uses for QSO_DATE, not a general ISO date string.
                        // Kept a couple of alternate names as a fallback in case
                        // POTA's API ever changes this.
                        string? dateStr = GetStringProperty(first, "qso_date", "date", "qsoDate", "activationDate");
                        if (dateStr != null)
                        {
                            if (DateTime.TryParseExact(dateStr, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
                                info.LastDate = exact;
                            else if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                                info.LastDate = parsed;
                        }
                    }
                }
            }
            catch
            {
                return null;
            }
            return info;
        }

        private static string? GetStringProperty(JsonElement element, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString();
            }
            return null;
        }

        private const string ActivationCacheFileName = "ActivationInfo.cache.csv";

        // How long a park's cached activation history is used as-is before
        // Show Map looks it up again - a once-a-day refresh. Activation
        // history only ever grows by the odd activation here and there, and
        // this is what lets the map open with no signal in the field.
        private static readonly TimeSpan ActivationCacheMaxAge = TimeSpan.FromHours(24);

        // After this many lookups in a row fail with none succeeding, the
        // connection is treated as down and every remaining park uses its
        // cached history straight away - otherwise a connection that hangs
        // rather than failing fast (e.g. a hotspot with no data) would make
        // Show Map wait out every park's 15-second timeout one batch at a
        // time.
        private const int ActivationGiveUpAfterFailures = 6;

        // The result of FetchActivationInfoAsync: every park's activation
        // info, how many parks' fresh lookups failed, and the oldest cached
        // copy used in their place (null if none had one) - so Show Map can
        // say when the history it's showing isn't current.
        public sealed record ActivationInfoResult(Dictionary<string, ActivationInfo> Info, int FailedCount, DateTime? OldestStaleUtc);

        // Looks up activation history for a whole list of parks at once, in
        // parallel (a handful of requests at a time, so we don't hammer
        // POTA's free API), preferring the local cache
        // (ActivationInfo.cache.csv) for any park looked up within the last
        // day. A lookup that fails falls back to that park's cached copy,
        // however old - only a park that has never been looked up ends up
        // with no history while offline.
        public static async Task<ActivationInfoResult> FetchActivationInfoAsync(
            HttpClient http, List<ParkRecord> parks, string cacheFolder, IProgress<int> progress)
        {
            var results = new System.Collections.Concurrent.ConcurrentDictionary<string, ActivationInfo>(StringComparer.OrdinalIgnoreCase);
            if (parks.Count == 0) return new ActivationInfoResult(new Dictionary<string, ActivationInfo>(StringComparer.OrdinalIgnoreCase), 0, null);

            var cache = LoadActivationCache(cacheFolder);
            var toFetch = new List<ParkRecord>();
            foreach (var park in parks)
            {
                if (cache.TryGetValue(park.Reference, out var cached) && DateTime.UtcNow - cached.FetchedUtc < ActivationCacheMaxAge)
                    results[park.Reference] = cached.Info;
                else
                    toFetch.Add(park);
            }

            DateTime? oldestStale = null;
            int unavailable = 0;
            if (toFetch.Count > 0)
            {
                int completed = 0, succeeded = 0, failed = 0;
                using var semaphore = new SemaphoreSlim(6);

                var tasks = toFetch.Select(async park =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        bool connectionLooksDown = Volatile.Read(ref succeeded) == 0 && Volatile.Read(ref failed) >= ActivationGiveUpAfterFailures;
                        var info = connectionLooksDown ? null : await GetActivationInfoAsync(http, park.Reference);
                        if (info != null)
                        {
                            Interlocked.Increment(ref succeeded);
                            lock (cache) cache[park.Reference] = (info, DateTime.UtcNow);
                            results[park.Reference] = info;
                        }
                        else
                        {
                            if (!connectionLooksDown) Interlocked.Increment(ref failed);
                            Interlocked.Increment(ref unavailable);
                            bool hasStale;
                            (ActivationInfo Info, DateTime FetchedUtc) stale;
                            lock (cache) hasStale = cache.TryGetValue(park.Reference, out stale);
                            results[park.Reference] = hasStale ? stale.Info : new ActivationInfo();
                            if (hasStale)
                            {
                                lock (cache)
                                {
                                    if (oldestStale == null || stale.FetchedUtc < oldestStale) oldestStale = stale.FetchedUtc;
                                }
                            }
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                        int done = Interlocked.Increment(ref completed);
                        progress.Report((int)(done * 100.0 / toFetch.Count));
                    }
                });

                await Task.WhenAll(tasks);
                SaveActivationCache(cacheFolder, cache);
            }
            else
            {
                progress.Report(100);
            }

            return new ActivationInfoResult(new Dictionary<string, ActivationInfo>(results, StringComparer.OrdinalIgnoreCase), unavailable, oldestStale);
        }

        // Same ';'-separated layout as AccessMethods.cache.csv. LastCallsign
        // can't contain ';' (callsigns are letters, digits and '/'), and
        // LastDate is yyyyMMdd or empty for a park never activated.
        private static Dictionary<string, (ActivationInfo Info, DateTime FetchedUtc)> LoadActivationCache(string cacheFolder)
        {
            var result = new Dictionary<string, (ActivationInfo, DateTime)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = Path.Combine(cacheFolder, ActivationCacheFileName);
                if (!File.Exists(path)) return result;

                foreach (string line in File.ReadAllLines(path).Skip(1)) // skip header
                {
                    string[] parts = line.Split(';');
                    if (parts.Length != 5 ||
                        !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ||
                        !DateTime.TryParse(parts[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fetched))
                        continue;
                    var info = new ActivationInfo { Count = count, LastCallsign = parts[2] };
                    if (DateTime.TryParseExact(parts[3], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var lastDate))
                        info.LastDate = lastDate;
                    result[parts[0]] = (info, fetched);
                }
            }
            catch
            {
                // Fails safe: just means less (or no) cache to work with this run.
            }
            return result;
        }

        private static void SaveActivationCache(string cacheFolder, Dictionary<string, (ActivationInfo Info, DateTime FetchedUtc)> cache)
        {
            try
            {
                Directory.CreateDirectory(cacheFolder);
                string path = Path.Combine(cacheFolder, ActivationCacheFileName);
                var lines = new List<string> { "Reference;Count;LastCallsign;LastDate;FetchedUtc" };
                lines.AddRange(cache.Select(kv =>
                    $"{kv.Key};{kv.Value.Info.Count.ToString(CultureInfo.InvariantCulture)};{kv.Value.Info.LastCallsign.Replace(";", "")};" +
                    $"{kv.Value.Info.LastDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? ""};{kv.Value.FetchedUtc:o}"));
                File.WriteAllLines(path, lines);
            }
            catch
            {
                // Best-effort - a failed save just means less is cached for next time.
            }
        }

        // ---- Boat-only access, from POTA's own park-info API ----------------------------

        // Distinct from ActivationsUrlBase above - that's per-QSO activation
        // history; this is the park's own metadata record, which (confirmed
        // against the live API) includes an admin-curated "accessMethods" tag
        // such as "Automobile,Foot" or "Boat" - exactly what a park's access
        // options are, maintained by POTA itself rather than guessed at here.
        private const string ParkInfoUrlBase = "https://api.pota.app/park/";
        private const string AccessMethodsCacheFileName = "AccessMethods.cache.csv";

        // Returns the raw accessMethods string ("Automobile,Boat,Foot", or ""
        // if POTA has no tag for this park), or null if the network call
        // itself failed - callers must treat those two differently: "" is a
        // confirmed answer worth caching, null means "couldn't check, don't
        // touch whatever's cached."
        public static async Task<string?> GetAccessMethodsAsync(HttpClient http, string reference)
        {
            try
            {
                string url = ParkInfoUrlBase + Uri.EscapeDataString(reference);
                // Same reasoning as GetActivationInfoAsync above - one park per
                // call, up to 6 concurrent (see FetchBoatAccessOnlyAsync), so a
                // hung request shouldn't get the full 100s default before this
                // method's own catch-and-continue behavior can kick in.
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
                string json = await http.GetStringAsync(url, cts.Token);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("accessMethods", out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString() ?? "";
                return "";
            }
            catch
            {
                return null;
            }
        }

        // True only when accessMethods lists Boat and nothing else -
        // "Boat,Foot" or "Automobile,Boat" means there's some other way in
        // too, so that doesn't count as boat-ONLY.
        public static bool IsBoatAccessOnly(string accessMethods)
        {
            if (string.IsNullOrWhiteSpace(accessMethods)) return false;
            var methods = accessMethods.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return methods.Length == 1 && methods[0].Equals("Boat", StringComparison.OrdinalIgnoreCase);
        }

        // This app is meant to be usable in the field, often with no signal
        // at all - ';' rather than ',' as the field separator since
        // accessMethods itself is comma-separated ("Automobile,Boat,Foot").
        private static Dictionary<string, (string AccessMethods, DateTime FetchedUtc)> LoadAccessMethodsCache(string cacheFolder)
        {
            var result = new Dictionary<string, (string, DateTime)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = Path.Combine(cacheFolder, AccessMethodsCacheFileName);
                if (!File.Exists(path)) return result;

                foreach (string line in File.ReadAllLines(path).Skip(1)) // skip header
                {
                    string[] parts = line.Split(';');
                    if (parts.Length == 3 &&
                        DateTime.TryParse(parts[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fetched))
                        result[parts[0]] = (parts[1], fetched);
                }
            }
            catch
            {
                // Fails safe: just means less (or no) cache to work with this run.
            }
            return result;
        }

        private static void SaveAccessMethodsCache(string cacheFolder, Dictionary<string, (string AccessMethods, DateTime FetchedUtc)> cache)
        {
            try
            {
                Directory.CreateDirectory(cacheFolder);
                string path = Path.Combine(cacheFolder, AccessMethodsCacheFileName);
                var lines = new List<string> { "Reference;AccessMethods;FetchedUtc" };
                lines.AddRange(cache.Select(kv => $"{kv.Key};{kv.Value.AccessMethods};{kv.Value.FetchedUtc:o}"));
                File.WriteAllLines(path, lines);
            }
            catch
            {
                // Best-effort - a failed save just means less is cached for next time.
            }
        }

        // Determines Boat Access Only for a whole list of parks, preferring a
        // local cache (AccessMethods.cache.csv) so this works fully offline
        // once a park's been looked up at least once before. Unlike the
        // other data this app caches (KFF, boundaries, the park list
        // itself), there's no weekly re-check here - a park's real-world
        // access method essentially never changes, so an existing cache
        // entry is treated as good permanently, not just for a week. The
        // only thing that causes a (re-)lookup is a park with NO cache
        // entry at all - new to this cache, most often because it's new to
        // POTA.
        // A lookup that fails (offline, etc.) leaves any existing cache entry
        // alone rather than treating it as "unknown" - only a park that's
        // NEVER been successfully looked up ends up unknown while offline.
        // Separate from this cache entirely: Form1's _boatAccessOnlyOverrides
        // (BoatAccessOnlyParks.csv, hand-edited - the grid itself is
        // read-only, there's no in-app way to toggle this) is layered on top
        // of whatever this method returns, for the rare real change (a
        // bridge washes out, POTA fixes a bad tag) this permanent caching
        // would otherwise miss.
        public static async Task<Dictionary<string, bool>> FetchBoatAccessOnlyAsync(
            HttpClient http, List<ParkRecord> parks, string cacheFolder, IProgress<int> progress)
        {
            var results = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (parks.Count == 0) return results;

            var cache = LoadAccessMethodsCache(cacheFolder);
            var toFetch = new List<ParkRecord>();
            foreach (var park in parks)
            {
                if (cache.TryGetValue(park.Reference, out var cached))
                    results[park.Reference] = IsBoatAccessOnly(cached.AccessMethods);
                else
                    toFetch.Add(park);
            }

            if (toFetch.Count == 0)
            {
                progress.Report(100);
                return results;
            }

            int completed = 0;
            using var semaphore = new SemaphoreSlim(6);

            var tasks = toFetch.Select(async park =>
            {
                await semaphore.WaitAsync();
                try
                {
                    string? accessMethods = await GetAccessMethodsAsync(http, park.Reference);
                    if (accessMethods != null)
                    {
                        // A real (possibly empty) answer - refresh the cache.
                        lock (cache) cache[park.Reference] = (accessMethods, DateTime.UtcNow);
                        results[park.Reference] = IsBoatAccessOnly(accessMethods);
                    }
                    else
                    {
                        // Couldn't reach it - fall back to a stale cache
                        // entry if there is one, otherwise genuinely unknown.
                        bool hasStale;
                        (string AccessMethods, DateTime FetchedUtc) stale;
                        lock (cache) hasStale = cache.TryGetValue(park.Reference, out stale);
                        results[park.Reference] = hasStale && IsBoatAccessOnly(stale.AccessMethods);
                    }
                }
                finally
                {
                    semaphore.Release();
                    int done = Interlocked.Increment(ref completed);
                    progress.Report((int)(done * 100.0 / toFetch.Count));
                }
            });

            await Task.WhenAll(tasks);
            SaveAccessMethodsCache(cacheFolder, cache);
            return results;
        }

        // ---- "Activated by me" history, straight from your ADIF file --------------------

        // Scans the ADIF log for every QSO record where YOU were the activator
        // (that's what the MY_SIG_INFO / MY_POTA_REF tag means - it only shows up
        // in your log when you were the one operating from that park). For each
        // park reference found this way, it collects every QSO_DATE it sees, so we
        // can later count "how many different days did I activate this park" and
        // find the most recent one.
        public static Dictionary<string, List<DateTime>> ParseMyActivationDates(string adifText)
        {
            var result = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);

            // ADIF records are separated by an <EOR> tag - split the log into one
            // chunk of text per QSO so we don't mix up fields from different QSOs.
            var records = Regex.Split(adifText, "<eor>", RegexOptions.IgnoreCase);
            var tagRegex = new Regex(@"<(?<tag>[a-zA-Z_]+)(:(?<len>\d+))?(:[a-zA-Z]+)?>", RegexOptions.IgnoreCase);

            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record)) continue;

                List<string>? myParkRefs = null;
                string? qsoDate = null;

                foreach (Match m in tagRegex.Matches(record))
                {
                    string tag = m.Groups["tag"].Value;
                    if (!m.Groups["len"].Success) continue;

                    int len = int.Parse(m.Groups["len"].Value);
                    int start = m.Index + m.Length;
                    if (start + len > record.Length) continue;

                    string value = record.Substring(start, len).Trim();
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    if (tag.Equals("MY_SIG_INFO", StringComparison.OrdinalIgnoreCase) ||
                        tag.Equals("MY_POTA_REF", StringComparison.OrdinalIgnoreCase))
                    {
                        // A record can legitimately carry both tags (older loggers
                        // wrote MY_POTA_REF; newer ones pair MY_SIG=POTA with
                        // MY_SIG_INFO) - collecting every occurrence, rather than
                        // keeping only the last one seen, means the full set of
                        // refs you've ever activated is simply this dictionary's
                        // keys, with no separate pass over the file needed for it.
                        (myParkRefs ??= new List<string>()).Add(value);
                    }
                    else if (tag.Equals("QSO_DATE", StringComparison.OrdinalIgnoreCase))
                    {
                        qsoDate = value;
                    }
                }

                // This QSO wasn't one where you were the activator - skip it.
                if (myParkRefs == null) continue;

                DateTime? parsedDate = null;
                if (qsoDate != null &&
                    DateTime.TryParseExact(qsoDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                {
                    parsedDate = d;
                }

                // A single QSO can list more than one park reference (a "two-fer" -
                // activating two parks at the same spot), separated by commas.
                foreach (string refValue in myParkRefs)
                {
                    foreach (string part in refValue.Split(','))
                    {
                        string cleaned = part.Trim();
                        if (string.IsNullOrWhiteSpace(cleaned)) continue;

                        if (!result.TryGetValue(cleaned, out var list))
                        {
                            list = new List<DateTime>();
                            result[cleaned] = list;
                        }
                        if (parsedDate.HasValue)
                            list.Add(parsedDate.Value);
                    }
                }
            }

            return result;
        }

        // ---- KFF (WWFF) cross-reference lookup ------------------------------------------

        // Reads a simple 2-column CSV mapping POTA references to KFF references, e.g.:
        //   POTA,KFF
        //   US-1234,KFF-0056
        //   US-5678,KFF-0102
        // The columns can be in either order (POTA,KFF or KFF,POTA) - this figures out
        // which is which by looking for the "KFF" prefix. Any header row is skipped
        // automatically. If the file is missing or a line can't be understood, that
        // line (or the whole file) is just skipped rather than causing an error - a
        // missing or partially-broken KFF file should never stop the rest of the app
        // from working.
        public static Dictionary<string, string> LoadKffCrossReference(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return result;

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch
            {
                return result;
            }

            foreach (var rawLine in SplitCsvRecords(text))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var fields = ParseCsvLine(line);
                if (fields.Count < 2) continue;

                string col1 = fields[0].Trim();
                string col2 = fields[1].Trim();
                if (string.IsNullOrWhiteSpace(col1) || string.IsNullOrWhiteSpace(col2)) continue;

                // Skip a header row such as "POTA,KFF" or "KFF,POTA".
                if (col1.Equals("POTA", StringComparison.OrdinalIgnoreCase) ||
                    col1.Equals("KFF", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string potaRef;
                string kffRef;
                if (col1.StartsWith("KFF", StringComparison.OrdinalIgnoreCase))
                {
                    kffRef = col1;
                    potaRef = col2;
                }
                else
                {
                    potaRef = col1;
                    kffRef = col2;
                }

                result[potaRef] = kffRef;
            }

            return result;
        }

        // Reads the KFF-reference -> name lookup written alongside
        // KffCrossReference.csv by WwffUpdateService.ConvertXlsToCsv
        // ("KFF,Name" - one row per distinct KFF reference, e.g.
        // "KFF-0019,Cumberland Gap (KY)"). Same tolerant behavior as
        // LoadKffCrossReference above: a missing or unreadable file just means
        // no names are available yet, not an error.
        public static Dictionary<string, string> LoadKffNames(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return result;

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch
            {
                return result;
            }

            foreach (var rawLine in SplitCsvRecords(text))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var fields = ParseCsvLine(line);
                if (fields.Count < 2) continue;

                string kffRef = fields[0].Trim();
                string name = fields[1].Trim();
                if (string.IsNullOrWhiteSpace(kffRef) || string.IsNullOrWhiteSpace(name)) continue;
                if (kffRef.Equals("KFF", StringComparison.OrdinalIgnoreCase)) continue; // header row

                result[kffRef] = name;
            }

            return result;
        }

        // A park that spans multiple states can have a combined KFF field like
        // "KFF-0019 (KY); KFF-4586 (TN); KFF-4587 (VA)" - one KFF ID per state
        // (see WwffUpdateService.ConvertXlsToCsv). Since the app only ever shows
        // parks for one state at a time, this picks out just the entry for
        // stateCode and strips the now-redundant "(XX)" label.
        //
        // Returns the value unchanged if there's nothing to split (the ordinary
        // single-KFF case), or if no segment's label matches stateCode - which
        // happens for the handful of National Forest entries WwffUpdateService
        // labels by sub-unit name instead of state (e.g. "KFF-1 (Bridger)"); with
        // no state code to match there, showing the full combined value is safer
        // than guessing which sub-unit is this state's.
        public static string SelectKffForState(string rawKff, string stateCode)
        {
            if (string.IsNullOrWhiteSpace(rawKff) || !rawKff.Contains(';'))
                return rawKff;

            string? match = null;
            int matchCount = 0;

            foreach (var rawSegment in rawKff.Split(';'))
            {
                string segment = rawSegment.Trim();
                int openParen = segment.LastIndexOf('(');
                int closeParen = segment.LastIndexOf(')');
                if (openParen < 0 || closeParen != segment.Length - 1 || closeParen <= openParen) continue;

                string label = segment.Substring(openParen + 1, closeParen - openParen - 1).Trim();
                if (label.Equals(stateCode, StringComparison.OrdinalIgnoreCase))
                {
                    match = segment.Substring(0, openParen).Trim();
                    matchCount++;
                }
            }

            return matchCount == 1 ? match! : rawKff;
        }

        // Splits raw CSV text into logical records (one per row) the same way
        // csv.Split('\n') does for the common case, except a '\n' that falls
        // inside a quoted field (a field value that itself contains a literal
        // newline) is treated as part of that field's value instead of ending
        // the record early, which a plain Split('\n') would get wrong.
        private static List<string> SplitCsvRecords(string csv)
        {
            var records = new List<string>();
            var rawLines = csv.Split('\n');
            string? pending = null;

            foreach (var rawLine in rawLines)
            {
                pending = pending == null ? rawLine : pending + "\n" + rawLine;

                // An even number of quote characters means every quoted field
                // opened so far has also been closed - this record is complete.
                // An odd count means we're still inside a quoted field that
                // contains a literal newline, so the next raw line is really a
                // continuation of this same record, not a new one.
                int quoteCount = pending.Length - pending.Replace("\"", "").Length;
                if (quoteCount % 2 == 0)
                {
                    records.Add(pending);
                    pending = null;
                }
            }

            if (pending != null) records.Add(pending);
            return records;
        }

        public static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                        inQuotes = true;
                    else if (c == ',')
                    {
                        fields.Add(sb.ToString());
                        sb.Clear();
                    }
                    else
                        sb.Append(c);
                }
            }
            fields.Add(sb.ToString());
            return fields;
        }

        public static void ExportCsv(string path, List<ParkRecord> parks)
        {
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            writer.WriteLine("Reference,Name,Latitude,Longitude,Grid,Elevation (ft),County,Xfer's,KFF,State,Completed");

            foreach (var p in parks)
            {
                writer.WriteLine(string.Join(",",
                    Csv(p.Reference),
                    Csv(p.Name),
                    p.Latitude.ToString(CultureInfo.InvariantCulture),
                    p.Longitude.ToString(CultureInfo.InvariantCulture),
                    Csv(p.Grid),
                    p.ElevationFeet.HasValue ? Math.Round(p.ElevationFeet.Value).ToString(CultureInfo.InvariantCulture) : "",
                    Csv(p.County),
                    Csv(p.Fers),
                    Csv(p.Kff),
                    Csv(p.State),
                    p.Completed ? "Yes" : "No"));
            }
        }

        public static void ExportExcel(string path, List<ParkRecord> parks)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Parks");

            string[] headers = { "Reference", "Name", "Latitude", "Longitude", "Grid", "Elevation (ft)", "County", "Xfer's", "KFF", "State", "Completed" };
            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cell(1, c + 1).Value = headers[c];
                ws.Cell(1, c + 1).Style.Font.Bold = true;
            }

            int row = 2;
            foreach (var p in parks)
            {
                ws.Cell(row, 1).Value = p.Reference;
                ws.Cell(row, 2).Value = p.Name;
                ws.Cell(row, 3).Value = p.Latitude;
                ws.Cell(row, 4).Value = p.Longitude;
                ws.Cell(row, 5).Value = p.Grid;
                if (p.ElevationFeet.HasValue)
                    ws.Cell(row, 6).Value = Math.Round(p.ElevationFeet.Value);
                ws.Cell(row, 7).Value = p.County;
                ws.Cell(row, 8).Value = p.Fers;
                ws.Cell(row, 9).Value = p.Kff;
                ws.Cell(row, 10).Value = p.State;
                ws.Cell(row, 11).Value = p.Completed ? "Yes" : "No";

                if (p.Completed)
                {
                    var rowRange = ws.Range(row, 1, row, 11);
                    rowRange.Style.Fill.BackgroundColor = p.OutOfState ? XLColor.Orange : XLColor.IndianRed;
                    rowRange.Style.Font.FontColor = XLColor.Black;
                    rowRange.Style.Font.Strikethrough = true;
                }

                row++;
            }

            ws.RangeUsed()!.SetAutoFilter();
            ws.Columns().AdjustToContents();
            ws.SheetView.FreezeRows(1);
            workbook.SaveAs(path);
        }

        private static string Csv(string field)
        {
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            return field;
        }
    }

    public record UsState(string Code, string Name);
}