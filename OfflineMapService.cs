using System.Net.Http;
using System.Text;

namespace PotaActivationTool
{
    // Per-state offline base maps for the park map - see the Offline Map
    // menu in Form1. Each state is one PMTiles vector map file (OpenStreetMap
    // data via the Protomaps basemap, clipped to the state's outline down to
    // house-number detail) published on this program's own GitHub release
    // tagged "offline-maps", rather than cut from Protomaps' daily builds on
    // the fly: Protomaps asks apps to copy their builds to their own storage
    // instead of hotlinking them. Downloaded once, kept until deleted - the
    // same "works with no signal in the field" goal as the rest of the
    // app's caches.
    public static class OfflineMapService
    {
        private const string ReleaseDownloadUrl =
            "https://github.com/K5JSG/POTA-Activation-Tool/releases/download/offline-maps/";

        // A download that receives no data at all for this long is treated
        // as stalled and abandoned - a whole-download timeout wouldn't work,
        // since a big state (California is over 1 GB) can legitimately take
        // a long time on a slow connection.
        private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

        // Every PMTiles v3 file starts with these 7 bytes - checked before a
        // finished download replaces an existing map, so an HTML error page
        // or truncated response can never overwrite a good file.
        private static readonly byte[] PmtilesMagic = Encoding.ASCII.GetBytes("PMTiles");

        // Separate from Form1's shared HttpClient on purpose: that one keeps
        // the default 100-second whole-request timeout, which a large map
        // download would blow through. Stalls are caught by StallTimeout
        // instead.
        private static readonly HttpClient DownloadClient = CreateDownloadClient();

        private static HttpClient CreateDownloadClient()
        {
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("POTA-Activation-Tool");
            return client;
        }

        public static string GetFolder(string appDataFolder)
        {
            string folder = Path.Combine(appDataFolder, "OfflineMaps");
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static string GetPath(string appDataFolder, string stateCode) =>
            Path.Combine(GetFolder(appDataFolder), stateCode.ToUpperInvariant() + ".pmtiles");

        public static bool IsDownloaded(string appDataFolder, string stateCode) =>
            !string.IsNullOrEmpty(stateCode) && File.Exists(GetPath(appDataFolder, stateCode));

        // State code -> file info for every state map currently downloaded.
        public static List<(string StateCode, FileInfo File)> ListDownloaded(string appDataFolder) =>
            Directory.GetFiles(GetFolder(appDataFolder), "*.pmtiles")
                .Select(p => (Path.GetFileNameWithoutExtension(p).ToUpperInvariant(), new FileInfo(p)))
                .OrderBy(t => t.Item1, StringComparer.Ordinal)
                .ToList();

        public static void Delete(string appDataFolder, string stateCode) =>
            File.Delete(GetPath(appDataFolder, stateCode));

        public static string FormatSize(long bytes) =>
            bytes >= 1024L * 1024 * 1024
                ? (bytes / (1024.0 * 1024 * 1024)).ToString("0.0") + " GB"
                : (bytes / (1024.0 * 1024)).ToString("0") + " MB";

        // Size of stateCode's published map, for the confirmation prompt
        // before downloading - null if it can't be found out (e.g. no
        // connection), in which case the prompt just leaves the size out.
        public static async Task<long?> GetDownloadSizeAsync(string stateCode)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var request = new HttpRequestMessage(HttpMethod.Head, DownloadUrl(stateCode));
                using var response = await DownloadClient.SendAsync(request, cts.Token);
                return response.IsSuccessStatusCode ? response.Content.Headers.ContentLength : null;
            }
            catch
            {
                return null;
            }
        }

        private static string DownloadUrl(string stateCode) =>
            ReleaseDownloadUrl + Uri.EscapeDataString(stateCode.ToUpperInvariant()) + ".pmtiles";

        // Downloads stateCode's map to a .part file next to its final
        // location, then swaps it in only once it's complete and looks like
        // a real PMTiles file - so a cancelled or failed download (or an
        // update of an already-downloaded state) never leaves a broken map
        // behind, and the previous copy keeps working until the new one is
        // fully in place. progress reports (bytes so far, total bytes if
        // the server sent a length).
        public static async Task DownloadAsync(string appDataFolder, string stateCode,
            IProgress<(long Received, long? Total)> progress, CancellationToken cancellationToken)
        {
            string finalPath = GetPath(appDataFolder, stateCode);
            string partPath = finalPath + ".part";
            string url = DownloadUrl(stateCode);

            try
            {
                using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                stallCts.CancelAfter(StallTimeout);

                using var response = await DownloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stallCts.Token);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new InvalidOperationException($"No offline map has been published for {stateCode} yet.");
                response.EnsureSuccessStatusCode();

                long? total = response.Content.Headers.ContentLength;
                await using (var source = await response.Content.ReadAsStreamAsync(stallCts.Token))
                await using (var target = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                {
                    var buffer = new byte[1 << 16];
                    long received = 0;
                    var lastReport = DateTime.MinValue;
                    while (true)
                    {
                        stallCts.CancelAfter(StallTimeout);
                        int read = await source.ReadAsync(buffer, stallCts.Token);
                        if (read == 0) break;
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        received += read;
                        if (DateTime.UtcNow - lastReport > TimeSpan.FromMilliseconds(250))
                        {
                            progress.Report((received, total));
                            lastReport = DateTime.UtcNow;
                        }
                    }
                    progress.Report((received, total));

                    if (total.HasValue && received != total.Value)
                        throw new IOException($"The download ended early ({FormatSize(received)} of {FormatSize(total.Value)}).");
                }

                if (!StartsWithPmtilesMagic(partPath))
                    throw new IOException("The downloaded file isn't a valid offline map.");

                File.Move(partPath, finalPath, overwrite: true);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryDelete(partPath);
                throw new TimeoutException("The download stalled - no data was received for " + (int)StallTimeout.TotalSeconds + " seconds.");
            }
            catch
            {
                TryDelete(partPath);
                throw;
            }
        }

        private static bool StartsWithPmtilesMagic(string path)
        {
            using var fs = File.OpenRead(path);
            var header = new byte[PmtilesMagic.Length];
            return fs.Read(header, 0, header.Length) == header.Length && header.AsSpan().SequenceEqual(PmtilesMagic);
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }
}
