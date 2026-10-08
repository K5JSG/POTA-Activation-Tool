using System.Globalization;
using System.Net.Http;
using System.Text;

namespace PotaActivationTool
{
    // One SOTA (Summits on the Air) summit, as published in SOTA's own public
    // summits list (see AllSummitsUrl below). Reference is a "SummitCode" like
    // "W4G/NG-001" - a different reference scheme from POTA's "K-1234", but
    // formatted similarly for display purposes elsewhere in this app.
    public class SotaSummit
    {
        public string Reference { get; set; } = "";
        public string Name { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double AltFt { get; set; }
        public int Points { get; set; }
        public int BonusPoints { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
        public int ActivationCount { get; set; }

        // Kept as SOTA's own dd/MM/yyyy text rather than parsed - unlike
        // ValidFrom/ValidTo, nothing here needs to compare or filter by this
        // date, only display it, and a blank value (never activated) is
        // common and simplest to represent as "".
        public string ActivationDate { get; set; } = "";
        public string ActivationCall { get; set; } = "";
    }

    public static class SotaService
    {
        // SOTA's own worldwide summit list - a single public CSV, no API key
        // or registration needed. See sota.org.uk/Joining-In/FAQs and
        // /Joining-In/Acceptable-Use-Policy (that page covers SOTAwatch/
        // Reflector forum moderation, not this file - there's no separate
        // published terms-of-use for the summits list itself).
        private const string AllSummitsUrl = "https://storage.sota.org.uk/summitslist.csv";
        private const string SummitListCacheFileName = "SotaSummits.cache.csv";
        private const string SummitListCacheInfoFileName = "SotaSummits.cache.info.txt";

        private static async Task<string> DownloadAllSummitsCsvAsync(HttpClient http)
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(60));
            return await http.GetStringAsync(AllSummitsUrl, cts.Token);
        }

        // SOTA's file's own first line is a title/date banner ("SOTA Summits
        // List (Date=...)"), not part of the CSV - real header is line 2.
        // Only US summits are kept (SummitCode starting with "W") - this app
        // is US-state-scoped throughout (see PotaService.FilterByState), and
        // dropping the other ~150,000 worldwide rows here means every later
        // parse of the cached file (one per state load) has ~6x less to read.
        //
        // Deliberately NOT filtered by ValidFrom/ValidTo here - that's a
        // look-up-time concern (see FilterCurrentlyValid) so the cached file
        // itself stays usable regardless of what day it's read on.
        private static List<SotaSummit> ParseSotaSummitsCsv(string csv)
        {
            var results = new List<SotaSummit>();
            var lines = csv.Split('\n');

            // Line 0 = title banner, line 1 = header - real data starts at 2.
            for (int i = 2; i < lines.Length; i++)
            {
                string line = lines[i].Trim('\r', '\n');
                if (string.IsNullOrWhiteSpace(line)) continue;

                var fields = PotaService.ParseCsvLine(line);
                if (fields.Count < 17) continue;

                string reference = fields[0];
                if (!reference.StartsWith("W", StringComparison.OrdinalIgnoreCase)) continue;

                if (!double.TryParse(fields[8], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)) continue;
                if (!double.TryParse(fields[9], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)) continue;
                if (!TryParseSotaDate(fields[12], out var validFrom)) continue;
                if (!TryParseSotaDate(fields[13], out var validTo)) continue;

                _ = int.TryParse(fields[10], NumberStyles.Integer, CultureInfo.InvariantCulture, out int points);
                _ = int.TryParse(fields[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out int bonusPoints);
                _ = double.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double altFt);
                _ = int.TryParse(fields[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out int activationCount);

                results.Add(new SotaSummit
                {
                    Reference = reference,
                    Name = fields[3],
                    Latitude = lat,
                    Longitude = lon,
                    AltFt = altFt,
                    Points = points,
                    BonusPoints = bonusPoints,
                    ValidFrom = validFrom,
                    ValidTo = validTo,
                    ActivationCount = activationCount,
                    ActivationDate = fields[15],
                    ActivationCall = fields[16]
                });
            }

            return results;
        }

        // SOTA publishes ValidFrom/ValidTo as dd/MM/yyyy (UK convention,
        // confirmed against real data - "31/12/2099" only parses that way,
        // not MM/dd/yyyy) - never use CultureInfo.CurrentCulture/default
        // parsing here, which would silently misread this on a US-locale
        // machine (exactly what this app runs on).
        private static bool TryParseSotaDate(string text, out DateTime value)
        {
            return DateTime.TryParseExact(
                text.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        // Returns the US SOTA summit list, preferring a local cache the same
        // way PotaService.GetAllParksAsync does: instant if young enough,
        // otherwise re-downloaded and re-cached, falling back to a stale
        // cache if the download fails. Unlike GetAllParksAsync, this never
        // throws - with neither a cache nor a network connection it returns
        // an empty list instead, since SOTA matching is a supplementary
        // feature (like boundary/trail Xfer detection) that a park list load
        // must succeed without, not an essential one.
        public static async Task<List<SotaSummit>> EnsureSummitsAsync(
            HttpClient http, string cacheFolder, TimeSpan maxAge, Action<string>? statusCallback = null)
        {
            string cachePath = Path.Combine(cacheFolder, SummitListCacheFileName);
            string infoPath = Path.Combine(cacheFolder, SummitListCacheInfoFileName);

            if (File.Exists(cachePath) && File.Exists(infoPath))
            {
                string infoText = "";
                try { infoText = File.ReadAllText(infoPath).Trim(); } catch { }

                if (DateTime.TryParse(infoText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var cachedTime)
                    && DateTime.UtcNow - cachedTime < maxAge)
                {
                    statusCallback?.Invoke("Using cached SOTA summit list...");
                    try
                    {
                        string cachedCsv = await File.ReadAllTextAsync(cachePath);
                        return ParseSotaSummitsCsv(cachedCsv);
                    }
                    catch
                    {
                        // Cache read failed unexpectedly - fall through and try a fresh download instead.
                    }
                }
            }

            try
            {
                statusCallback?.Invoke("Downloading latest summit list from SOTA...");
                string csv = await DownloadAllSummitsCsvAsync(http);
                var summits = ParseSotaSummitsCsv(csv);

                try
                {
                    Directory.CreateDirectory(cacheFolder);
                    // Cache only the already-US-filtered rows (see
                    // ParseSotaSummitsCsv), not SOTA's full worldwide file -
                    // this app never needs summits outside the US, and every
                    // later read of this cache has ~6x less to parse.
                    var lines = new List<string>
                    {
                        "SummitCode,AssociationName,RegionName,SummitName,AltM,AltFt,GridRef1,GridRef2,Longitude,Latitude,Points,BonusPoints,ValidFrom,ValidTo,ActivationCount,ActivationDate,ActivationCall"
                    };
                    lines.AddRange(summits.Select(SummitToCsvRow));
                    await File.WriteAllLinesAsync(cachePath, lines, Encoding.UTF8);
                    await File.WriteAllTextAsync(infoPath, DateTime.UtcNow.ToString("o"));
                }
                catch
                {
                    // Not being able to write the cache isn't fatal - we still have
                    // the data in memory for this run.
                }

                return summits;
            }
            catch
            {
                if (File.Exists(cachePath))
                {
                    statusCallback?.Invoke("Couldn't reach SOTA - using last cached summit list.");
                    string cachedCsv = await File.ReadAllTextAsync(cachePath);
                    return ParseSotaSummitsCsv(cachedCsv);
                }

                // No cache and no network - unlike PotaService.GetAllParksAsync
                // (where the park list is essential and there's nothing
                // sensible to do without it), SOTA matching is a supplementary
                // feature exactly like boundary/trail Xfer detection - see
                // EnsureBoundariesAsync/EnsureTrailRoutesAsync, which likewise
                // degrade to an empty list rather than throw. A park list load
                // must still succeed offline even the very first time SOTA
                // data has never been cached.
                return new List<SotaSummit>();
            }
        }

        // Re-serializes one already-parsed summit back into the same column
        // order ParseSotaSummitsCsv reads - only the columns this app actually
        // uses are round-tripped (AssociationName/RegionName/GridRef1/
        // GridRef2/AltM are never read, see ParseSotaSummitsCsv, so they're
        // left blank rather than carried through unnecessarily).
        private static string SummitToCsvRow(SotaSummit s)
        {
            string[] fields =
            {
                s.Reference, "", "", CsvField(s.Name), "", s.AltFt.ToString(CultureInfo.InvariantCulture), "", "",
                s.Longitude.ToString(CultureInfo.InvariantCulture), s.Latitude.ToString(CultureInfo.InvariantCulture),
                s.Points.ToString(CultureInfo.InvariantCulture), s.BonusPoints.ToString(CultureInfo.InvariantCulture),
                s.ValidFrom.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                s.ValidTo.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                s.ActivationCount.ToString(CultureInfo.InvariantCulture), s.ActivationDate, s.ActivationCall
            };
            return string.Join(",", fields);
        }

        private static string CsvField(string field)
        {
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            return field;
        }

        // Only a summit whose ValidFrom/ValidTo window covers today is a
        // real, currently-activatable SOTA reference - applied at call time
        // (not baked into the cache) so a summit that becomes valid/invalid
        // as the calendar turns doesn't need a fresh download to notice.
        public static List<SotaSummit> FilterCurrentlyValid(List<SotaSummit> summits)
        {
            var today = DateTime.UtcNow.Date;
            return summits.Where(s => s.ValidFrom <= today && today <= s.ValidTo).ToList();
        }

        // Narrows the full US summit list down to just the ones actually IN
        // the given state - otherwise the map's ""SOTA Summits"" layer would
        // show every US summit nationwide. An earlier version used a padded
        // bounding box around the loaded parks' own coordinates instead of
        // this - too loose for a state like New York, whose irregular shape
        // borders five other states within a short distance of much of its
        // own territory, so it kept pulling in NH/VT/MA/CT/PA summits too.
        //
        // Uses CountyLookupService.FindCounty - the same offline, real
        // county-boundary point-in-polygon test (counties.json, Census
        // cartographic boundary data) already used to assign each POTA
        // park's own county - rather than trusting SOTA's own
        // AssociationName/RegionName text: several US associations cover
        // multiple states with inconsistent per-region labeling (see
        // ParseSotaSummitsCsv's "W"-prefix filtering above for the same
        // reasoning), so those fields alone can't reliably answer "which
        // state is this summit actually in."
        //
        // Only meant for narrowing what the MAP draws - a real "does this
        // summit fall inside this park's boundary" match
        // (FerLookupService.ComputeSotaMatches) should keep testing the
        // full, unfiltered summit list, so a legitimate match can never be
        // missed just because a park's own boundary polygon happens to dip
        // slightly across a state line.
        public static List<SotaSummit> FilterByState(List<SotaSummit> summits, string stateCode)
        {
            var results = new List<SotaSummit>();
            foreach (var summit in summits)
            {
                var (_, summitState) = CountyLookupService.FindCounty(summit.Latitude, summit.Longitude);
                if (string.Equals(summitState, stateCode, StringComparison.OrdinalIgnoreCase))
                    results.Add(summit);
            }
            return results;
        }
    }
}
