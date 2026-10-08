using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PotaActivationTool
{
    // One stop on a rove plan. Reference is blank for a stop that isn't a
    // park - an address, or a point the route line was dragged to.
    public class RoveStop
    {
        public string Reference { get; set; } = "";
        public string Name { get; set; } = "";
        public double Lat { get; set; }
        public double Lon { get; set; }
    }

    public class RovePlan
    {
        public string Name { get; set; } = "";
        public DateTime Updated { get; set; }
        public bool StartAtGps { get; set; }
        // Set on a plan loaded from pota.app (PotaAccountService), so loading
        // the same one again updates this copy instead of adding another.
        public int? PotaRoveId { get; set; }
        public List<RoveStop> Stops { get; set; } = new();
    }

    // Storage and road routing for the map's rove planner (MapAssets/rove.js,
    // served through MapServer's /rove routes):
    //  - plans are one JSON file each under RovePlans\ in the app data
    //    folder, so they survive upgrades and work with no signal
    //  - driving routes come from the public OSRM server (OpenStreetMap
    //    roads, no account or key), one leg per pair of consecutive stops.
    //    Every leg is cached permanently under RouteCache\, keyed by its two
    //    end points, so a route seen once with signal still draws in the
    //    field, and reordering stops only needs the legs that are new.
    public static class RoveService
    {
        private const string OsrmUrl = "https://router.project-osrm.org/route/v1/driving/";

        // OSRM's public server asks for light use: one request at a time,
        // and no more than about one a second.
        private static readonly SemaphoreSlim OsrmGate = new(1, 1);
        private static DateTime _lastOsrmCallUtc = DateTime.MinValue;

        private static readonly HttpClient RouteClient = CreateRouteClient();

        private static HttpClient CreateRouteClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("POTA-Activation-Tool");
            return client;
        }

        // Plan ids are made by the map page (rove.js newPlanId); anything else
        // is refused, so an id can never reach outside the plans folder.
        private static readonly Regex PlanIdPattern = new("^[a-z0-9]{6,40}$", RegexOptions.Compiled);

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private static string GetPlansFolder(string appDataFolder)
        {
            string folder = Path.Combine(appDataFolder, "RovePlans");
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static string GetRouteCacheFolder(string appDataFolder)
        {
            string folder = Path.Combine(appDataFolder, "RouteCache");
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static bool IsValidPlanId(string id) => PlanIdPattern.IsMatch(id);

        // Every saved plan's id, name, last change and stop count, newest first.
        public static string ListPlansJson(string appDataFolder)
        {
            var list = new List<(string Id, RovePlan Plan)>();
            foreach (string path in Directory.GetFiles(GetPlansFolder(appDataFolder), "*.json"))
            {
                var plan = ReadPlan(path);
                if (plan != null) list.Add((Path.GetFileNameWithoutExtension(path), plan));
            }
            return JsonSerializer.Serialize(list
                .OrderByDescending(p => p.Plan.Updated)
                .Select(p => new { id = p.Id, name = p.Plan.Name, updated = p.Plan.Updated, stopCount = p.Plan.Stops.Count, potaRoveId = p.Plan.PotaRoveId }), JsonOptions);
        }

        public static string? GetPlanJson(string appDataFolder, string id)
        {
            var plan = ReadPlan(Path.Combine(GetPlansFolder(appDataFolder), id + ".json"));
            return plan == null ? null : JsonSerializer.Serialize(plan, JsonOptions);
        }

        // Saves the plan the page sent, after a round trip through RovePlan so
        // only the expected fields are ever written. Returns the saved plan.
        public static string SavePlan(string appDataFolder, string id, string json)
        {
            var plan = JsonSerializer.Deserialize<RovePlan>(json, JsonOptions) ?? throw new InvalidDataException("Empty plan");
            plan.Name = string.IsNullOrWhiteSpace(plan.Name) ? "Rove" : plan.Name.Trim();
            plan.Updated = DateTime.UtcNow;
            string saved = JsonSerializer.Serialize(plan, JsonOptions);
            string path = Path.Combine(GetPlansFolder(appDataFolder), id + ".json");
            // Written to a temp file and swapped in, so a crash mid-save
            // can't leave a half-written plan behind.
            string temp = path + ".tmp";
            File.WriteAllText(temp, saved);
            File.Move(temp, path, true);
            return saved;
        }

        public static void DeletePlan(string appDataFolder, string id)
        {
            string path = Path.Combine(GetPlansFolder(appDataFolder), id + ".json");
            if (File.Exists(path)) File.Delete(path);
        }

        // Every saved plan plus the cached road route of each leg between
        // consecutive stops, for a saved copy of the map (MapServer's
        // /save-map) - it has no app behind it to ask. Legs are keyed
        // "lon,lat;lon,lat" exactly like the route cache, so rove.js can look
        // them up the same way. Only what's cached: nothing is fetched here.
        public static string BuildEmbedJson(string appDataFolder, string selectedPlanId)
        {
            string cacheFolder = GetRouteCacheFolder(appDataFolder);
            var plans = new JsonArray();
            var legs = new JsonObject();
            foreach (string path in Directory.GetFiles(GetPlansFolder(appDataFolder), "*.json"))
            {
                var plan = ReadPlan(path);
                if (plan == null) continue;
                var node = JsonSerializer.SerializeToNode(plan, JsonOptions)!.AsObject();
                node["id"] = Path.GetFileNameWithoutExtension(path);
                plans.Add(node);
                for (int i = 0; i + 1 < plan.Stops.Count; i++)
                {
                    var from = (Math.Round(plan.Stops[i].Lon, 5), Math.Round(plan.Stops[i].Lat, 5));
                    var to = (Math.Round(plan.Stops[i + 1].Lon, 5), Math.Round(plan.Stops[i + 1].Lat, 5));
                    string key = FormatPoint(from) + ";" + FormatPoint(to);
                    if (legs.ContainsKey(key)) continue;
                    var leg = ReadCachedLeg(cacheFolder, from, to);
                    if (leg != null) legs[key] = leg;
                }
            }
            return new JsonObject
            {
                ["plans"] = plans,
                ["legs"] = legs,
                ["selected"] = IsValidPlanId(selectedPlanId) ? selectedPlanId : null
            }.ToJsonString();
        }

        private static RovePlan? ReadPlan(string path)
        {
            try
            {
                return File.Exists(path) ? JsonSerializer.Deserialize<RovePlan>(File.ReadAllText(path), JsonOptions) : null;
            }
            catch
            {
                return null; // unreadable file - left alone, just not listed
            }
        }

        // ---- Routing

        // coords: "lon,lat;lon,lat;..." for the stops in driving order.
        // Returns { legs: [leg or null, ...], online: bool }, one leg per
        // consecutive pair. A leg is null only when it isn't cached and OSRM
        // couldn't be reached (no signal) or found no road route; the page
        // draws those as straight lines.
        public static async Task<string> GetRouteJsonAsync(string appDataFolder, string coords)
        {
            var points = ParsePoints(coords);
            int legCount = Math.Max(0, points.Count - 1);
            var legs = new JsonNode?[legCount];
            string cacheFolder = GetRouteCacheFolder(appDataFolder);

            var missing = new List<int>();
            for (int i = 0; i < legCount; i++)
            {
                legs[i] = ReadCachedLeg(cacheFolder, points[i], points[i + 1]);
                if (legs[i] == null) missing.Add(i);
            }

            bool online = true;
            if (missing.Count > 0)
            {
                // One request for the whole route first - it returns every leg
                // at once. If OSRM answered but couldn't route all of it (a
                // stop out on an island, say), the missing legs are tried one
                // at a time so the rest still get road routes.
                var all = await FetchLegsAsync(points);
                if (all.Reached && all.Legs != null)
                {
                    foreach (int i in missing)
                    {
                        legs[i] = all.Legs[i];
                        WriteCachedLeg(cacheFolder, points[i], points[i + 1], all.Legs[i]);
                    }
                }
                else if (all.Reached)
                {
                    foreach (int i in missing)
                    {
                        var one = await FetchLegsAsync(new List<(double, double)> { points[i], points[i + 1] });
                        if (!one.Reached) { online = false; break; }
                        if (one.Legs == null) continue;
                        legs[i] = one.Legs[0];
                        WriteCachedLeg(cacheFolder, points[i], points[i + 1], one.Legs[0]);
                    }
                }
                else
                {
                    online = false;
                }
            }

            var result = new JsonObject { ["legs"] = new JsonArray(legs.Select(l => l?.DeepClone()).ToArray()), ["online"] = online };
            return result.ToJsonString();
        }

        private static List<(double Lon, double Lat)> ParsePoints(string coords)
        {
            var points = new List<(double, double)>();
            foreach (string pair in coords.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split(',');
                if (parts.Length != 2
                    || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)
                    || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat))
                    throw new FormatException("Bad coordinate: " + pair);
                points.Add((Math.Round(lon, 5), Math.Round(lat, 5)));
            }
            if (points.Count > 100) throw new FormatException("Too many stops");
            return points;
        }

        private static string FormatPoint((double Lon, double Lat) p) =>
            p.Lon.ToString("0.#####", CultureInfo.InvariantCulture) + "," + p.Lat.ToString("0.#####", CultureInfo.InvariantCulture);

        private static string LegCachePath(string cacheFolder, (double, double) from, (double, double) to)
        {
            string key = FormatPoint(from) + ";" + FormatPoint(to);
            string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
            return Path.Combine(cacheFolder, hash + ".json");
        }

        private static JsonNode? ReadCachedLeg(string cacheFolder, (double, double) from, (double, double) to)
        {
            try
            {
                string path = LegCachePath(cacheFolder, from, to);
                return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : null;
            }
            catch
            {
                return null; // damaged cache file - fetched again
            }
        }

        private static void WriteCachedLeg(string cacheFolder, (double, double) from, (double, double) to, JsonNode? leg)
        {
            if (leg == null) return;
            try
            {
                File.WriteAllText(LegCachePath(cacheFolder, from, to), leg.ToJsonString());
            }
            catch
            {
                // Not cached this time - it's just fetched again next time.
            }
        }

        // Reached = OSRM answered at all. Legs = null when it answered but
        // found no route (e.g. "NoRoute"), else one entry per leg.
        private static async Task<(bool Reached, JsonNode?[]? Legs)> FetchLegsAsync(List<(double Lon, double Lat)> points)
        {
            string url = OsrmUrl + string.Join(";", points.Select(FormatPoint)) +
                "?overview=false&steps=true&geometries=geojson";

            await OsrmGate.WaitAsync();
            try
            {
                var wait = _lastOsrmCallUtc.AddSeconds(1) - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait);
                _lastOsrmCallUtc = DateTime.UtcNow;

                string body;
                try
                {
                    using var response = await RouteClient.GetAsync(url);
                    body = await response.Content.ReadAsStringAsync();
                }
                catch
                {
                    return (false, null); // no signal / server down
                }

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (!root.TryGetProperty("code", out var code) || code.GetString() != "Ok") return (true, null);
                var legsJson = root.GetProperty("routes")[0].GetProperty("legs");
                var legs = legsJson.EnumerateArray().Select(l => (JsonNode?)ConvertLeg(l)).ToArray();
                return (true, legs.Length == points.Count - 1 ? legs : null);
            }
            catch
            {
                return (true, null); // answered with something unexpected
            }
            finally
            {
                OsrmGate.Release();
            }
        }

        // OSRM leg -> what the page needs: distance (m), duration (s), the road
        // path as [lat, lon] pairs, and turn-by-turn steps as plain text.
        private static JsonObject ConvertLeg(JsonElement leg)
        {
            var path = new JsonArray();
            var steps = new JsonArray();
            double[]? lastPoint = null;

            foreach (var step in leg.GetProperty("steps").EnumerateArray())
            {
                foreach (var c in step.GetProperty("geometry").GetProperty("coordinates").EnumerateArray())
                {
                    double lon = Math.Round(c[0].GetDouble(), 5), lat = Math.Round(c[1].GetDouble(), 5);
                    if (lastPoint != null && lastPoint[0] == lat && lastPoint[1] == lon) continue;
                    lastPoint = new[] { lat, lon };
                    path.Add(new JsonArray(lat, lon));
                }

                string? text = DescribeStep(step);
                if (text != null)
                    steps.Add(new JsonObject { ["text"] = text, ["distance"] = Math.Round(step.GetProperty("distance").GetDouble()) });
            }

            return new JsonObject
            {
                ["distance"] = Math.Round(leg.GetProperty("distance").GetDouble()),
                ["duration"] = Math.Round(leg.GetProperty("duration").GetDouble()),
                ["path"] = path,
                ["steps"] = steps
            };
        }

        private static readonly string[] CompassNames = { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" };

        // English text for one OSRM maneuver, close to what the OSRM demo
        // site shows. Returns null for steps not worth a line of their own.
        private static string? DescribeStep(JsonElement step)
        {
            var maneuver = step.GetProperty("maneuver");
            string type = maneuver.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            string modifier = maneuver.TryGetProperty("modifier", out var m) ? m.GetString() ?? "" : "";
            string road = RoadName(step);
            string onto = road.Length > 0 ? " onto " + road : "";
            string turn = modifier switch
            {
                "uturn" => "Make a U-turn",
                "straight" => "Go straight",
                "" => "Turn",
                _ => "Turn " + modifier
            };

            switch (type)
            {
                case "depart":
                    int bearing = maneuver.TryGetProperty("bearing_after", out var b) ? b.GetInt32() : 0;
                    return "Head " + CompassNames[(int)Math.Round(bearing / 45.0) % 8] + (road.Length > 0 ? " on " + road : "");
                case "arrive":
                    return "Arrive" + (modifier is "left" or "right" ? " (on the " + modifier + ")" : "");
                case "turn":
                    // "Go straight" through an unnamed junction says nothing useful.
                    if (modifier == "straight") return road.Length > 0 ? "Continue straight onto " + road : null;
                    return turn + onto;
                case "end of road":
                    return turn + " at the end of the road" + onto;
                case "new name":
                case "continue":
                    if (modifier is "" or "straight") return road.Length > 0 ? "Continue onto " + road : null;
                    return (modifier == "uturn" ? "Make a U-turn" : "Continue " + modifier) + onto;
                case "merge":
                    return "Merge" + (modifier.Length > 0 && modifier != "straight" ? " " + modifier : "") + onto;
                case "on ramp":
                    return "Take the ramp" + (modifier.Contains("left") ? " on the left" : modifier.Contains("right") ? " on the right" : "") + onto;
                case "off ramp":
                    return "Take the exit" + (modifier.Contains("left") ? " on the left" : modifier.Contains("right") ? " on the right" : "") + onto;
                case "fork":
                    return "Keep " + (modifier.Contains("left") ? "left" : modifier.Contains("right") ? "right" : "straight") + " at the fork" + onto;
                case "roundabout":
                case "rotary":
                    int exit = maneuver.TryGetProperty("exit", out var e) ? e.GetInt32() : 0;
                    return "At the " + (type == "rotary" ? "traffic circle" : "roundabout") +
                        (exit > 0 ? ", take the " + Ordinal(exit) + " exit" : "") + onto;
                case "roundabout turn":
                    return "At the roundabout, " + turn.ToLowerInvariant() + onto;
                case "exit roundabout":
                case "exit rotary":
                case "notification":
                    return null;
                default:
                    return road.Length > 0 ? "Continue onto " + road : null;
            }
        }

        // "Ridge Road (NY 104)", or whichever of the two OSRM has.
        private static string RoadName(JsonElement step)
        {
            string name = step.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            string reference = step.TryGetProperty("ref", out var r) ? r.GetString() ?? "" : "";
            reference = reference.Replace(";", " / ");
            if (name.Length > 0 && reference.Length > 0 && !name.Contains(reference)) return name + " (" + reference + ")";
            return name.Length > 0 ? name : reference;
        }

        private static string Ordinal(int n) =>
            n + ((n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    }
}
