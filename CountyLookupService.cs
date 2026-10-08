using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace PotaActivationTool
{
    // Looks up which US county a latitude/longitude point falls in, entirely
    // offline, using county boundary shapes bundled with the app (counties.json,
    // derived from the Census Bureau's own 2017 cartographic boundary files via
    // the widely-used "us-atlas" dataset). This replaces per-park calls to the
    // Census geocoding API, which is what used to make loading a state's parks
    // slow - hundreds of network round trips, one per park. Now it's a handful of
    // milliseconds of local math per park instead.
    public static class CountyLookupService
    {
        private class CountyData
        {
            public string Fips { get; set; } = "";
            public string Name { get; set; } = "";
            public string StateFips { get; set; } = "";
            public double MinLon { get; set; }
            public double MinLat { get; set; }
            public double MaxLon { get; set; }
            public double MaxLat { get; set; }
            // Polys[polygon index][ring index] = flat [lon,lat,lon,lat,...] array.
            // A county can have more than one polygon (e.g. islands). Within a
            // polygon, the first ring is the outer boundary and any further rings
            // are holes.
            public List<List<double[]>> Polys { get; set; } = new();
        }

        // Standard FIPS state numeric code -> two-letter postal abbreviation.
        // This is fixed, decades-old federal reference data - not something that
        // changes or needs updating.
        private static readonly Dictionary<string, string> FipsToState = new()
        {
            ["01"] = "AL", ["02"] = "AK", ["04"] = "AZ", ["05"] = "AR", ["06"] = "CA",
            ["08"] = "CO", ["09"] = "CT", ["10"] = "DE", ["11"] = "DC", ["12"] = "FL",
            ["13"] = "GA", ["15"] = "HI", ["16"] = "ID", ["17"] = "IL", ["18"] = "IN",
            ["19"] = "IA", ["20"] = "KS", ["21"] = "KY", ["22"] = "LA", ["23"] = "ME",
            ["24"] = "MD", ["25"] = "MA", ["26"] = "MI", ["27"] = "MN", ["28"] = "MS",
            ["29"] = "MO", ["30"] = "MT", ["31"] = "NE", ["32"] = "NV", ["33"] = "NH",
            ["34"] = "NJ", ["35"] = "NM", ["36"] = "NY", ["37"] = "NC", ["38"] = "ND",
            ["39"] = "OH", ["40"] = "OK", ["41"] = "OR", ["42"] = "PA", ["44"] = "RI",
            ["45"] = "SC", ["46"] = "SD", ["47"] = "TN", ["48"] = "TX", ["49"] = "UT",
            ["50"] = "VT", ["51"] = "VA", ["53"] = "WA", ["54"] = "WV", ["55"] = "WI",
            ["56"] = "WY", ["60"] = "AS", ["66"] = "GU", ["69"] = "MP", ["72"] = "PR",
            ["78"] = "VI"
        };

        private static List<CountyData>? _counties;
        private static readonly object _lock = new();

        // Match "fips" (JSON) to "Fips" (C# property) etc. - without this, every
        // county silently deserializes with blank/zero values instead of throwing
        // an error, which is exactly what caused counties to show up empty: every
        // county's bounding box collapsed to (0,0)-(0,0), so no real park
        // coordinate could ever match any county.
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        // Loads counties.json (shipped next to the .exe) into memory the first
        // time it's needed, then reuses it for the rest of the app's run. This
        // file is read-only application data (not user data), so it's fine for it
        // to live in the install folder even under Program Files.
        private static List<CountyData> EnsureLoaded()
        {
            lock (_lock)
            {
                if (_counties != null) return _counties;

                string path = Path.Combine(Application.StartupPath, "counties.json");
                string json = File.ReadAllText(path);
                _counties = JsonSerializer.Deserialize<List<CountyData>>(json, JsonOptions) ?? new List<CountyData>();
                return _counties;
            }
        }

        // Returns (CountyName, StateAbbreviation) for a given point, or ("", "")
        // only in the extremely unlikely case that no county data could be
        // loaded at all. Every real point gets a real county: the primary
        // point-in-polygon test handles the vast majority of cases, and a
        // nearest-boundary fallback (see FindNearestCounty below) catches
        // points that fall just outside a county's simplified/generalized
        // outline - almost always small islands, narrow peninsulas, or
        // barrier islands (Statue of Liberty, Robert Moses SP, etc.), where
        // the boundary data's precision doesn't perfectly trace the real
        // coastline. Unlike elevation, there's no legitimate ambiguity here:
        // every real park is unambiguously inside exactly one county, so
        // falling back to "whichever county's boundary is closest" is a safe
        // correction, not a guess.
        public static (string County, string StateAbbrev) FindCounty(double latitude, double longitude)
        {
            List<CountyData> counties;
            try
            {
                counties = EnsureLoaded();
            }
            catch
            {
                // If counties.json is missing or unreadable, fail safe with a blank
                // result rather than crashing the whole park-loading flow.
                return ("", "");
            }

            foreach (var county in counties)
            {
                if (longitude < county.MinLon || longitude > county.MaxLon ||
                    latitude < county.MinLat || latitude > county.MaxLat)
                {
                    continue;
                }

                if (PointInCounty(longitude, latitude, county))
                {
                    string stateAbbrev = FipsToState.TryGetValue(county.StateFips, out var abbr) ? abbr : "";
                    return (county.Name, stateAbbrev);
                }
            }

            if (counties.Count > 0)
            {
                var nearest = FindNearestCounty(longitude, latitude, counties);
                if (nearest != null)
                {
                    string stateAbbrev = FipsToState.TryGetValue(nearest.StateFips, out var abbr) ? abbr : "";
                    return (nearest.Name, stateAbbrev);
                }
            }

            return ("", "");
        }

        // The state a point is actually inside ("" if none) - for the GPS
        // receiver's position (see Form1's automatic State dropdown choice).
        // Unlike FindCounty, deliberately no nearest-county fallback: a GPS
        // fix out in a lake, over the border in Canada, or anywhere else
        // outside every county should pick no state at all, not whichever
        // one happens to be closest.
        public static string FindStateContaining(double latitude, double longitude)
        {
            try
            {
                foreach (var county in EnsureLoaded())
                {
                    if (longitude < county.MinLon || longitude > county.MaxLon ||
                        latitude < county.MinLat || latitude > county.MaxLat)
                        continue;
                    if (PointInCounty(longitude, latitude, county))
                        return FipsToState.TryGetValue(county.StateFips, out var abbr) ? abbr : "";
                }
            }
            catch
            {
                // counties.json missing/unreadable - no state, same as FindCounty.
            }
            return "";
        }

        // Finds the county whose boundary is geometrically closest to the
        // point, used only when the point isn't strictly inside any county's
        // polygon. Checks distance to every edge segment of every county -
        // this only runs for the rare unmatched case (not for every park), so
        // the extra cost is negligible in practice.
        private static CountyData? FindNearestCounty(double lon, double lat, List<CountyData> counties)
        {
            CountyData? best = null;
            double bestDistSq = double.MaxValue;

            foreach (var county in counties)
            {
                foreach (var poly in county.Polys)
                {
                    foreach (var ring in poly)
                    {
                        int n = ring.Length / 2;
                        for (int i = 0; i < n; i++)
                        {
                            int next = (i + 1) % n;
                            double ax = ring[2 * i], ay = ring[2 * i + 1];
                            double bx = ring[2 * next], by = ring[2 * next + 1];

                            double distSq = PointToSegmentDistanceSquared(lon, lat, ax, ay, bx, by);
                            if (distSq < bestDistSq)
                            {
                                bestDistSq = distSq;
                                best = county;
                            }
                        }
                    }
                }
            }

            return best;
        }

        private static double PointToSegmentDistanceSquared(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay;
            if (dx == 0 && dy == 0)
            {
                double ddx = px - ax, ddy = py - ay;
                return ddx * ddx + ddy * ddy;
            }

            double t = ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy);
            t = Math.Max(0, Math.Min(1, t));

            double cx = ax + t * dx, cy = ay + t * dy;
            double ex = px - cx, ey = py - cy;
            return ex * ex + ey * ey;
        }

        private static bool PointInCounty(double lon, double lat, CountyData county)
        {
            foreach (var poly in county.Polys)
            {
                // Standard point-in-polygon parity test, applied across every ring
                // of this polygon (outer boundary plus any holes). A point crosses
                // an odd number of ring boundaries only when it's genuinely inside
                // the shape - this handles holes correctly without needing to know
                // in advance which ring is the outer one and which are holes.
                int ringsContainingPoint = 0;
                foreach (var ring in poly)
                {
                    if (PointInRing(lon, lat, ring)) ringsContainingPoint++;
                }
                if (ringsContainingPoint % 2 == 1) return true;
            }
            return false;
        }

        private static bool PointInRing(double lon, double lat, double[] ring)
        {
            int n = ring.Length / 2;
            bool inside = false;
            int j = n - 1;
            for (int i = 0; i < n; i++)
            {
                double xi = ring[2 * i], yi = ring[2 * i + 1];
                double xj = ring[2 * j], yj = ring[2 * j + 1];

                if (((yi > lat) != (yj > lat)) &&
                    (lon < (xj - xi) * (lat - yi) / (yj - yi) + xi))
                {
                    inside = !inside;
                }
                j = i;
            }
            return inside;
        }
    }
}
