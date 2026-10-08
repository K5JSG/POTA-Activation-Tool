using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PotaActivationTool
{
    // Looks up elevation for a park by its POTA reference, using a
    // pre-generated lookup table (ParkElevations.csv) built by querying a real
    // elevation API for every park's exact coordinate - see the
    // GenerateParkElevations tool (Tools/GenerateParkElevations).
    //
    // This is far more accurate than a bundled raster grid could ever be at a
    // reasonable size: it only needs data for the actual, finite set of park
    // locations - not continuous coverage of the whole country - so there's
    // no resolution/file-size tradeoff, no interpolation, and no coastline
    // artifacts to correct for. It's still fully offline at runtime: the
    // lookup table is generated once (or refreshed occasionally by re-running
    // that tool) and just read from disk, with no network call needed while
    // using the app.
    public static class ElevationLookupService
    {
        private static Dictionary<string, double>? _elevations;
        private static readonly object _lock = new();

        // Loads (or reloads) the elevation lookup table from the given CSV
        // path. Safe to call even if the file doesn't exist yet - lookups
        // then just always return null, the same as if a park's reference
        // simply isn't in the table.
        public static void Load(string csvPath)
        {
            lock (_lock)
            {
                var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    if (File.Exists(csvPath))
                    {
                        var lines = File.ReadAllLines(csvPath);
                        for (int i = 1; i < lines.Length; i++) // skip header row
                        {
                            string line = lines[i].Trim();
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            int comma = line.IndexOf(',');
                            if (comma < 0) continue;

                            string reference = line.Substring(0, comma).Trim();
                            string valueStr = line.Substring(comma + 1).Trim();

                            if (reference.Length > 0 &&
                                double.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double feet))
                            {
                                result[reference] = feet;
                            }
                        }
                    }
                }
                catch
                {
                    // Missing or unreadable file just means no elevation data
                    // is available yet - not a reason to stop the app working.
                }

                _elevations = result;
            }
        }

        // Returns the elevation in feet for a park reference, or null if
        // that reference isn't in the lookup table (e.g. a brand-new park
        // added since the table was last generated/refreshed).
        public static double? GetElevationFeet(string reference)
        {
            var elevations = _elevations;
            if (elevations == null || string.IsNullOrEmpty(reference)) return null;
            return elevations.TryGetValue(reference, out var feet) ? feet : (double?)null;
        }
    }
}
