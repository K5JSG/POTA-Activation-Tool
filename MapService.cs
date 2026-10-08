using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace PotaActivationTool
{
    // One park's worth of information, packaged up exactly the way the map page needs it.
    // The [JsonPropertyName] attributes control what the field is called in the JSON/JavaScript
    // that gets embedded in the HTML file - JavaScript convention is lowerCamelCase.
    public class MapParkDto
    {
        [JsonPropertyName("reference")]
        public string Reference { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("lat")]
        public double Lat { get; set; }

        [JsonPropertyName("lon")]
        public double Lon { get; set; }

        [JsonPropertyName("county")]
        public string County { get; set; } = "";

        [JsonPropertyName("elevationFeet")]
        public double? ElevationFeet { get; set; }

        [JsonPropertyName("kff")]
        public string Kff { get; set; } = "";

        // True = you have activated this park yourself (green pin).
        // False = you have not activated it yet (yellow pin, or orange if
        // BoatAccessOnly - see that field).
        [JsonPropertyName("completed")]
        public bool Completed { get; set; }

        // User-flagged (grid checkbox, not derived from any data source) as
        // only reachable by boat. Shown as an orange pin, but only while not
        // yet completed - completed status (green) still wins once you've
        // actually activated it, same priority order as the grid's row
        // coloring.
        [JsonPropertyName("boatAccessOnly")]
        public bool BoatAccessOnly { get; set; }

        [JsonPropertyName("communityCount")]
        public int CommunityCount { get; set; }

        [JsonPropertyName("communityCallsign")]
        public string CommunityCallsign { get; set; } = "";

        [JsonPropertyName("communityDate")]
        public string CommunityDate { get; set; } = "";

        [JsonPropertyName("myCount")]
        public int MyCount { get; set; }

        [JsonPropertyName("myDate")]
        public string MyDate { get; set; } = "";
    }

    // One SOTA summit, for the optional "SOTA Summits" map layer - see
    // Form1's _sotaSummits (already filtered to currently-valid US summits)
    // and FerLookupService.ComputeSotaMatches, which is what actually decides
    // which park's row gets a SOTA Ref value. This layer shows every loaded
    // summit regardless of that match, since a summit just outside every
    // park's boundary is still useful to see on the map.
    public class MapSotaSummitDto
    {
        [JsonPropertyName("reference")]
        public string Reference { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("lat")]
        public double Lat { get; set; }

        [JsonPropertyName("lon")]
        public double Lon { get; set; }

        [JsonPropertyName("altFeet")]
        public double AltFeet { get; set; }

        [JsonPropertyName("points")]
        public int Points { get; set; }

        [JsonPropertyName("activationCount")]
        public int ActivationCount { get; set; }

        [JsonPropertyName("activationCall")]
        public string ActivationCall { get; set; } = "";

        [JsonPropertyName("activationDate")]
        public string ActivationDate { get; set; } = "";
    }

    // One toggleable map layer - e.g. NY's "PAD-US" or "EC" area layers, or a
    // single named national trail. Mirrors how potamap.us (github.com/cwhelchel/
    // potamap.ol) groups its own boundary data into named, independently
    // checkbox-toggled layers - see FerLookupService.BoundaryFeature.Layer.
    public class MapBoundaryLayerDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        // True for a trail (rendered as a line) rather than an area boundary
        // (rendered as a filled polygon).
        [JsonPropertyName("isLine")]
        public bool IsLine { get; set; }

        [JsonPropertyName("features")]
        public List<MapGeoFeatureDto> Features { get; set; } = new();
    }

    // One boundary polygon or trail route within a layer.
    public class MapGeoFeatureDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        // Geometry[part][ring][point] = [lon, lat]. For an area layer this is
        // GeoJSON MultiPolygon coordinates (a part's first ring is its outer
        // boundary, any further rings are holes - mirrors
        // FerLookupService.BoundaryFeature.Polys). For a line layer, each part
        // holds exactly one "ring", which is really just that line's own point
        // sequence (no ring/hole concept for a line) - reusing the same shape
        // so the map's JS has a single rendering code path for both.
        [JsonPropertyName("geometry")]
        public List<List<double[][]>> Geometry { get; set; } = new();

        // Mirrors FerLookupService.BoundaryFeature/TrailRoute's own
        // Min/Max fields - a cheap reject before the real point-in-polygon
        // (or point-near-trail) math for the ""am I in/near this park right
        // now"" live GPS check (see MapService's InfoBar JS), the same
        // bounding-box-first pattern that code already uses server-side.
        // Recomputing these from Geometry in JS on every load would be
        // redundant work for data already computed once here.
        [JsonPropertyName("minLon")]
        public double MinLon { get; set; }
        [JsonPropertyName("minLat")]
        public double MinLat { get; set; }
        [JsonPropertyName("maxLon")]
        public double MaxLon { get; set; }
        [JsonPropertyName("maxLat")]
        public double MaxLat { get; set; }
    }

    public static class MapService
    {
        // Builds one complete HTML page (map + all the data + all the JavaScript
        // needed to draw it) as a single string, served by the app's local map
        // server (MapServer) - no install, no account, no API key. It uses
        // Leaflet (a free open-source mapping library) built into the app, and
        // either OpenStreetMap/Esri tiles online or the state's downloaded
        // offline map. See ToStandaloneHtml for the Save Map copy.
        public static string BuildMapHtml(
            List<MapParkDto> parks, List<MapBoundaryLayerDto>? boundaryLayers = null,
            List<MapSotaSummitDto>? sotaSummits = null)
        {
            var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
            string parkJson = JsonSerializer.Serialize(parks, jsonOptions);
            string boundaryJson = JsonSerializer.Serialize(boundaryLayers ?? new List<MapBoundaryLayerDto>(), jsonOptions);
            string sotaJson = JsonSerializer.Serialize(sotaSummits ?? new List<MapSotaSummitDto>(), jsonOptions);
            string cqZoneJson = EnsureZoneJsonLoaded("cqZones.json", ref _cqZoneJson);
            string ituZoneJson = EnsureZoneJsonLoaded("ituZones.json", ref _ituZoneJson);

            // Guard against a park/boundary/summit name that happens to contain
            // "</script>" - that would otherwise break out of our embedded
            // <script> block.
            parkJson = parkJson.Replace("</", "<\\/");
            boundaryJson = boundaryJson.Replace("</", "<\\/");
            sotaJson = sotaJson.Replace("</", "<\\/");

            string html = HtmlTemplate
                // The program's logo as the browser tab icon - inlined as a
                // data URI rather than served, so a Save Map copy opened
                // from disk shows it too.
                .Replace("__FAVICON__", "data:image/png;base64," + Convert.ToBase64String(MapServer.ReadAssetBytes("favicon.png")))
                .Replace("__LEAFLET_CSS__", MapServer.AssetPrefix + "leaflet/leaflet.css")
                .Replace("__LEAFLET_JS__", MapServer.AssetPrefix + "leaflet/leaflet.js")
                .Replace("__PROTOMAPS_JS__", MapServer.AssetPrefix + "protomaps-leaflet/protomaps-leaflet.js")
                .Replace("__OSM_STYLE__", MapServer.ReadAssetText("osm-style.js").Replace("</", "<\\/"))
                .Replace("__ROVE_JS__", MapServer.ReadAssetText("rove.js").Replace("</", "<\\/"))
                .Replace("__DIRECT_GPS_JS__", MapServer.ReadAssetText("direct-gps.js").Replace("</", "<\\/"))
                .Replace("__PARK_DATA__", parkJson)
                .Replace("__BOUNDARY_DATA__", boundaryJson)
                .Replace("__SOTA_DATA__", sotaJson)
                .Replace("__CQZONE_DATA__", cqZoneJson)
                .Replace("__ITUZONE_DATA__", ituZoneJson);
            return html;
        }

        // The map page as served loads Leaflet and protomaps-leaflet from the
        // app's own map server (so it opens with no internet). A copy saved
        // with Save Map is opened later straight from disk, with no server
        // behind it, so it points back at the same library versions on their
        // public CDNs instead - it needs a connection for the map tiles
        // anyway, exactly as saved maps always have.
        //
        // Its Street layer is Esri's World Street Map instead of
        // OpenStreetMap's tiles: OSM's servers block tile requests that
        // don't say which website they're for (its tile usage policy), and a
        // page opened from a file can't send that, so a saved copy showed
        // "Access blocked" on every tile (seen 2026-10-08). Esri's servers -
        // the same free, no-account service as the Satellite layer - don't
        // ask. The live map, served from http://127.0.0.1, keeps OSM.
        private const string OsmStreetTiles = "'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png'";
        private const string OsmStreetAttribution = "attribution: '&copy; <a href=\"https://www.openstreetmap.org/copyright\">OpenStreetMap</a> contributors'";

        public static string ToStandaloneHtml(string servedHtml, string? roveEmbedJson = null, string? gpsEmbedJson = null) =>
            servedHtml
                .Replace("var embeddedGps = null;", "var embeddedGps = " + (gpsEmbedJson ?? "null").Replace("</", "<\\/") + ";")
                .Replace("var embeddedRove = null;", "var embeddedRove = " + (roveEmbedJson ?? "null").Replace("</", "<\\/") + ";")
                .Replace(MapServer.AssetPrefix + "leaflet/leaflet.css", "https://unpkg.com/leaflet@1.9.4/dist/leaflet.css")
                .Replace(MapServer.AssetPrefix + "leaflet/leaflet.js", "https://unpkg.com/leaflet@1.9.4/dist/leaflet.js")
                .Replace(MapServer.AssetPrefix + "protomaps-leaflet/protomaps-leaflet.js", "https://unpkg.com/protomaps-leaflet@5.1.0/dist/protomaps-leaflet.js")
                .Replace(OsmStreetTiles, "'https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}'")
                .Replace(OsmStreetAttribution, "attribution: 'Tiles &copy; Esri &mdash; Source: Esri, HERE, Garmin, USGS, NGA, EPA, USDA, NPS'");

        // CQ/ITU zone boundary polygons (cqZones.json/ituZones.json, shipped next
        // to the .exe like counties.json) - simplified from HB9HIL's MIT-licensed
        // hamradio-zones-geojson dataset down to a compact [zoneNumber, MultiPolygon
        // coordinates] array per zone, just precise enough for the mouse-position
        // readout below to pick the right zone. Read once and cached for the rest
        // of the app's run, then embedded verbatim into every generated map's HTML
        // (same reasoning as parkJson etc above) so the map stays a single
        // self-contained file that still works when saved and reopened offline.
        private static string? _cqZoneJson;
        private static string? _ituZoneJson;

        private static string EnsureZoneJsonLoaded(string fileName, ref string? cache)
        {
            if (cache != null) return cache;
            try
            {
                cache = File.ReadAllText(Path.Combine(Application.StartupPath, fileName)).Replace("</", "<\\/");
            }
            catch
            {
                // Missing/unreadable file - fail safe with an empty zone list rather
                // than breaking the whole map; the readout just shows no zone.
                cache = "[]";
            }
            return cache;
        }

        private const string HtmlTemplate = @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"" />
<title>POTA Activation Tool - Park Map</title>
<link rel=""icon"" type=""image/png"" href=""__FAVICON__"" />
<meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
<link rel=""stylesheet"" href=""__LEAFLET_CSS__"" />
<style>
  html, body { margin: 0; padding: 0; height: 100%; font-family: Segoe UI, Arial, sans-serif; }
  #map { position: absolute; top: 0; left: 0; right: 0; bottom: 0; }
  .gps-status {
    position: absolute; bottom: 24px; left: 12px; z-index: 1000;
    background: white; padding: 8px 12px; border-radius: 6px;
    box-shadow: 0 1px 5px rgba(0,0,0,0.4); font-size: 12px;
  }
  .legend-swatch {
    display: inline-block; width: 12px; height: 12px; border-radius: 50%;
    margin-right: 6px; border: 1px solid #333; vertical-align: middle;
  }
  .pota-popup a { color: #1a5fb4; text-decoration: none; font-weight: bold; }
  .pota-popup a:hover { text-decoration: underline; }
  .pota-popup .my-line { margin-top: 6px; color: #2e8b22; font-weight: bold; }
  .pota-popup .boat-line { margin-top: 6px; color: #d45500; font-weight: bold; }
  .boundary-popup-layer { margin-top: 2px; font-size: 11px; opacity: 0.7; }
  .layer-swatch {
    display: inline-block; width: 11px; height: 11px;
    margin-right: 6px; vertical-align: middle; border: 1px solid rgba(0,0,0,0.35);
  }
  .layer-count { opacity: 0.6; }
  .layer-tree-row {
    display: flex; align-items: center; cursor: pointer;
    padding: 2px 0; white-space: nowrap;
  }
  .layer-tree-group-header .layer-tree-row { padding: 0; flex: 1 1 auto; }
  .layer-tree-toggle {
    display: inline-block; width: 14px; flex: 0 0 auto;
    text-align: center; cursor: pointer; user-select: none; font-size: 10px;
  }
  .layer-tree-children { margin-left: 16px; }
  .recenter-control a { color: #1a73e8; }
  .recenter-control a svg { vertical-align: -4px; }
  .recenter-control a.waiting { opacity: 0.5; cursor: wait; }
  .recenter-control a.following { background-color: #2e7d32; color: #fff; }
  .recenter-control a.following:hover { background-color: #1b5e20; }
  .coord-box {
    position: absolute; top: 12px; left: 12px; z-index: 1001;
    background: white; padding: 8px 12px; border-radius: 6px;
    box-shadow: 0 1px 5px rgba(0,0,0,0.4); font-size: 12px; line-height: 1.45;
    white-space: nowrap;
  }
  .coord-box span { font-weight: bold; }
  .sidebar-toggle {
    position: absolute; top: 128px; left: 12px; z-index: 1001;
    background: white; border: none; border-radius: 4px;
    padding: 8px 10px; font-size: 16px; line-height: 1; cursor: pointer;
    box-shadow: 0 1px 5px rgba(0,0,0,0.4);
  }
  .sidebar {
    position: absolute; top: 128px; left: 12px; bottom: 24px; z-index: 1000;
    width: 240px; max-width: calc(100vw - 24px);
    background: white; border-radius: 6px; box-shadow: 0 1px 5px rgba(0,0,0,0.4);
    overflow-y: auto; font-size: 13px;
  }
  .sidebar-header {
    display: flex; align-items: center; justify-content: space-between;
    padding: 8px 10px; font-weight: bold;
    border-bottom: 1px solid rgba(0,0,0,0.1);
    position: sticky; top: 0; background: inherit;
  }
  .sidebar-header button {
    background: none; border: none; font-size: 14px; cursor: pointer; color: inherit;
  }
  .sidebar-section { padding: 8px 10px; border-bottom: 1px solid rgba(0,0,0,0.08); }
  .sidebar-section:last-child { border-bottom: none; }
  .sidebar-section[hidden] { display: none; }
  .sidebar-heading {
    font-weight: bold; margin-bottom: 4px; font-size: 11px; opacity: 0.65;
    text-transform: uppercase; letter-spacing: .04em;
  }
  .sidebar-row { display: flex; align-items: center; padding: 3px 0; cursor: pointer; white-space: nowrap; }
  .sidebar-row input { margin: 0 6px 0 0; }
  .sidebar-row[hidden], .sidebar-note[hidden] { display: none; }
  .sidebar-note { font-size: 11px; opacity: 0.7; padding: 3px 0 0 0; max-width: 190px; line-height: 1.35; }
  .info-bar {
    position: absolute; top: 12px; left: 50%; transform: translateX(-50%); z-index: 1002;
    max-width: min(600px, calc(100vw - 24px));
    background: white; padding: 8px 16px; border-radius: 6px;
    box-shadow: 0 1px 5px rgba(0,0,0,0.4); font-size: 13px; line-height: 1.5;
    text-align: center;
  }
  .info-bar.info-bar-active { background: #2e8b22; color: white; font-weight: bold; }
  .context-menu {
    position: fixed; z-index: 2000;
    background: white; border-radius: 6px; box-shadow: 0 2px 8px rgba(0,0,0,0.35);
    padding: 4px 0; font-size: 13px; min-width: 170px;
  }
  .context-menu-item { padding: 8px 14px; cursor: pointer; white-space: nowrap; }
  .context-menu-item:hover { background: #f0f0f0; }
  .measure-panel {
    position: absolute; bottom: 24px; left: 50%; transform: translateX(-50%); z-index: 1002;
    background: white; padding: 8px 14px; border-radius: 6px;
    box-shadow: 0 1px 5px rgba(0,0,0,0.4); font-size: 13px;
    display: flex; align-items: center; gap: 10px;
  }
  .measure-panel[hidden] { display: none; }
  .measure-panel button { background: none; border: none; font-size: 14px; cursor: pointer; color: inherit; padding: 0; }
  #map.measuring-cursor { cursor: crosshair; }
  .direct-gps-button {
    position: absolute; bottom: 64px; left: 12px; z-index: 1000;
    background: white; border: none; border-radius: 6px; padding: 7px 12px;
    font: bold 12px Segoe UI, Arial, sans-serif; cursor: pointer;
    box-shadow: 0 1px 5px rgba(0,0,0,0.4);
  }
  .direct-gps-button[hidden] { display: none; }
  .rove-toggle {
    position: absolute; top: 12px; right: 12px; z-index: 1001;
    background: white; border: none; border-radius: 4px;
    padding: 7px 12px; font-size: 13px; font-weight: bold; cursor: pointer;
    box-shadow: 0 1px 5px rgba(0,0,0,0.4); font-family: inherit;
  }
  .rove-panel {
    position: absolute; top: 12px; right: 12px; bottom: 130px; z-index: 1000;
    width: 300px; max-width: calc(100vw - 24px);
    background: white; border-radius: 6px; box-shadow: 0 1px 5px rgba(0,0,0,0.4);
    overflow-y: auto; overflow-x: hidden; font-size: 13px;
  }
  .rove-panel[hidden], .rove-toggle[hidden] { display: none; }
  .rove-panel button, .rove-panel select, .rove-panel input[type=text] { font-family: inherit; font-size: 12px; }
  .rove-line { display: flex; flex-wrap: wrap; gap: 6px; margin: 4px 0; align-items: center; }
  .rove-line > select, .rove-line > input[type=text] { flex: 1 1 auto; min-width: 0; padding: 3px 4px; }
  .rove-line > button { flex: 0 0 auto; padding: 3px 8px; cursor: pointer; }
  .rove-line > button.rove-danger { color: #b3261e; }
  .rove-results { margin: 2px 0 4px 0; }
  .rove-results div { padding: 3px 4px; cursor: pointer; border-radius: 3px; }
  .rove-results div:hover { background: rgba(0,0,0,0.07); }
  .rove-summary { font-weight: bold; margin-bottom: 4px; }
  .rove-stops { list-style: none; margin: 0; padding: 0; }
  .rove-stop { display: flex; align-items: center; gap: 6px; padding: 4px 2px; cursor: grab; border-top: 2px solid transparent; }
  .rove-stop.drop-before { border-top-color: #1a73e8; }
  .rove-stop .rove-name { flex: 1 1 auto; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; cursor: pointer; }
  .rove-stop .rove-name b { font-family: Consolas, monospace; font-weight: bold; }
  .rove-stop button { background: none; border: none; cursor: pointer; color: #b3261e; font-size: 13px; padding: 0 2px; }
  .rove-num {
    flex: 0 0 auto; width: 20px; height: 20px; border-radius: 50%; background: #00796b; color: white;
    font-size: 11px; font-weight: bold; display: inline-flex; align-items: center; justify-content: center;
  }
  .rove-leg { margin: 0 0 2px 26px; font-size: 11px; opacity: 0.75; cursor: pointer; }
  .rove-leg:hover { opacity: 1; text-decoration: underline; }
  .rove-steps { margin: 2px 0 6px 26px; padding-left: 16px; font-size: 11px; }
  .rove-steps li { margin: 1px 0; }
  .rove-steps span { opacity: 0.6; }
  .rove-stop-icon div {
    width: 22px; height: 22px; border-radius: 50%; background: #00796b; color: white; border: 2px solid white;
    box-shadow: 0 0 3px rgba(0,0,0,0.7); font: bold 12px Segoe UI, Arial, sans-serif;
    display: flex; align-items: center; justify-content: center; box-sizing: border-box;
  }
  .rove-popup-btn { margin-top: 8px; padding: 3px 10px; cursor: pointer; font-family: inherit; }
  .rove-stop-place div { background: #5c6bc0; cursor: move; }
  .rove-route-line { cursor: grab; }
  .save-map-link { display: inline-block; padding: 3px 0; color: #1a5fb4; font-weight: bold; text-decoration: none; }
  .save-map-link:hover { text-decoration: underline; }
  @media (prefers-color-scheme: dark) {
    .rove-toggle, .rove-panel { background: #2d2d30; color: #e8e8e8; box-shadow: 0 1px 5px rgba(0,0,0,0.6); }
    .rove-results div:hover { background: rgba(255,255,255,0.1); }
    .rove-stop button, .rove-line > button.rove-danger { color: #ff8a80; }
  }
  @media (prefers-color-scheme: dark) {
    body { background: #1e1e1e; }
    .gps-status { background: #2d2d30; color: #e8e8e8; box-shadow: 0 1px 5px rgba(0,0,0,0.6); }
    .direct-gps-button { background: #2d2d30; color: #e8e8e8; box-shadow: 0 1px 5px rgba(0,0,0,0.6); }
    .coord-box { background: #2d2d30; color: #e8e8e8; box-shadow: 0 1px 5px rgba(0,0,0,0.6); }
    .info-bar { background: #2d2d30; color: #e8e8e8; box-shadow: 0 1px 5px rgba(0,0,0,0.6); }
    .info-bar.info-bar-active { background: #2e8b22; color: white; }
    .legend-swatch { border-color: #999; }
    .pota-popup a { color: #6ab0f3; }
    .pota-popup .my-line { color: #6fdc6f; }
    .pota-popup .boat-line { color: #ff9a4d; }
    .layer-swatch { border-color: rgba(255,255,255,0.4); }
    .sidebar-toggle, .sidebar {
      background: #2d2d30; color: #e8e8e8;
      box-shadow: 0 1px 5px rgba(0,0,0,0.6);
    }
    .sidebar-header { border-bottom-color: rgba(255,255,255,0.15); }
    .sidebar-section { border-bottom-color: rgba(255,255,255,0.12); }
    .context-menu { background: #2d2d30; color: #e8e8e8; box-shadow: 0 2px 8px rgba(0,0,0,0.6); }
    .context-menu-item:hover { background: #3f3f42; }
    .measure-panel { background: #2d2d30; color: #e8e8e8; box-shadow: 0 1px 5px rgba(0,0,0,0.6); }
  }
</style>
</head>
<body>
<div id=""map""></div>
<div class=""coord-box"" id=""coordBox"">
  Grid: <span id=""coordGrid"">–</span><br>
  ITU: <span id=""coordItu"">–</span><br>
  CQ: <span id=""coordCq"">–</span><br>
  Lat: <span id=""coordLat"">–</span><br>
  Long: <span id=""coordLong"">–</span>
</div>
<div class=""gps-status"" id=""gpsStatus"" hidden></div>
<div class=""info-bar"" id=""infoBar"" hidden></div>
<div class=""measure-panel"" id=""measurePanel"" hidden>
  <span id=""measureDistanceText"">Distance: 0 ft</span>
  <button id=""measureCloseBtn"" title=""Clear measurement"">✕</button>
</div>
<div class=""context-menu"" id=""mapContextMenu"" hidden></div>

<button id=""roveToggle"" class=""rove-toggle"" title=""Plan a rove: pick parks, get a driving route"">Rove Planner</button>
<div id=""rovePanel"" class=""rove-panel"" hidden>
  <div class=""sidebar-header"">
    <span>Rove Planner</span>
    <button id=""roveClose"" title=""Hide the rove planner"">✕</button>
  </div>
  <div class=""sidebar-section"">
    <div class=""sidebar-heading"">Plan</div>
    <div class=""rove-line""><select id=""rovePlanSelect"" title=""Pick a saved plan to load it""></select></div>
    <div class=""rove-line""><input type=""text"" id=""rovePlanName"" placeholder=""Plan name"" title=""Rename this plan"" /></div>
    <div class=""rove-line"">
      <button id=""roveNew"" title=""Start a new, empty plan"">New</button>
      <button id=""roveDelete"" class=""rove-danger"" title=""Delete this plan"">Delete</button>
    </div>
    <div class=""rove-line"">
      <button id=""rovePotaLoad"" title=""List the rove plans saved on your pota.app account (POTA Login in the app's menu bar)"">From pota.app</button>
      <select id=""rovePotaSelect"" hidden title=""Pick a pota.app rove plan to load it""></select>
    </div>
    <div class=""sidebar-note"" id=""roveNote"" hidden></div>
  </div>
  <div class=""sidebar-section"">
    <div class=""sidebar-heading"">Add Stops</div>
    <div class=""rove-line"">
      <input type=""text"" id=""roveAddInput"" placeholder=""US-1234, park name or address"" />
      <button id=""roveAddBtn"">Add</button>
    </div>
    <div class=""rove-results"" id=""roveAddResults""></div>
    <div class=""sidebar-note"">Or click a park on the map and use its Add to Rove button, or drag the route line to add a stop there. Blue stops (addresses) can be dragged to move them.</div>
  </div>
  <div class=""sidebar-section"">
    <div class=""sidebar-heading"">Route</div>
    <label class=""sidebar-row""><input type=""checkbox"" id=""roveStartGps"" /> Start from my location</label>
    <div class=""rove-line"">
      <button id=""roveOrder"" title=""Reorder the stops into the shortest trip"">Order by Distance</button>
      <button id=""roveClear"" class=""rove-danger"" title=""Remove every stop from this plan"">Remove All</button>
      <button id=""rovePrint"" title=""Open the turn-by-turn directions as a printable page - choose Save as PDF in the print window for a PDF"">Print Directions</button>
    </div>
    <div class=""rove-summary"" id=""roveSummary""></div>
    <ol class=""rove-stops"" id=""roveStops""></ol>
  </div>
</div>

<button id=""sidebarToggle"" class=""sidebar-toggle"" title=""Show/hide layers panel"">☰</button>
<div id=""sidebar"" class=""sidebar"">
  <div class=""sidebar-header"">
    <span>Layers</span>
    <button id=""sidebarClose"" title=""Hide layers panel"">✕</button>
  </div>
  <div class=""sidebar-section"">
    <div class=""sidebar-heading"">Base Map</div>
    <label class=""sidebar-row"" id=""baseLayerOfflineRow"" hidden><input type=""radio"" name=""baseLayer"" id=""baseLayerOffline"" /> <span id=""baseLayerOfflineLabel"">Offline</span></label>
    <label class=""sidebar-row""><input type=""radio"" name=""baseLayer"" id=""baseLayerStreet"" checked /> Street (online)</label>
    <label class=""sidebar-row""><input type=""radio"" name=""baseLayer"" id=""baseLayerSatellite"" /> Satellite (online)</label>
    <div class=""sidebar-note"" id=""offlineMapNote"" hidden></div>
  </div>
  <div class=""sidebar-section"">
    <div class=""sidebar-heading"">Show</div>
    <label class=""sidebar-row""><input type=""checkbox"" id=""overlayWorked"" checked /> Worked</label>
    <label class=""sidebar-row""><input type=""checkbox"" id=""overlayNotWorked"" checked /> Not worked</label>
    <label class=""sidebar-row""><input type=""checkbox"" id=""overlaySota"" /> SOTA Summits</label>
    <div class=""sidebar-note"" id=""zoomInNote"" hidden>Zoom in closer to see park boundaries, trails and SOTA summits.</div>
  </div>
  <div class=""sidebar-section"" id=""boundaryLayerSection"" hidden></div>
  <div class=""sidebar-section"" id=""saveMapSection"" hidden>
    <div class=""sidebar-heading"">Save</div>
    <a id=""saveMapLink"" class=""save-map-link"" href=""/save-map"" download>Save a copy of this map</a>
    <div class=""sidebar-note"">An .html file you can open later without the app - with your rove plans and the driving routes already looked up. It uses the online Street and Satellite maps.</div>
  </div>
  <div class=""sidebar-section"">
    <div class=""sidebar-heading"">Legend</div>
    <div><span class=""legend-swatch"" style=""background:#FFD500;""></span>Not yet activated by me</div>
    <div><span class=""legend-swatch"" style=""background:#FF6700;""></span>Boat access only, not yet activated</div>
    <div><span class=""legend-swatch"" style=""background:#2E8B22;""></span>Activated by me</div>
    <div><span class=""legend-swatch"" style=""background:#1a73e8;""></span>Your location</div>
    <div><span class=""legend-swatch"" style=""background:#8B4513;""></span>SOTA summit</div>
  </div>
</div>

<script src=""__LEAFLET_JS__""></script>
<script src=""__PROTOMAPS_JS__""></script>
<script>
__OSM_STYLE__
</script>
<script>
// Filled in only in a saved copy of the map (MapService.ToStandaloneHtml):
// where to ask the app for the GPS receiver's position, with the key the
// app requires from pages it didn't serve itself - see pollGps.
var embeddedGps = null;
</script>
<script>
var parkData = __PARK_DATA__;
var boundaryLayers = __BOUNDARY_DATA__;
var sotaData = __SOTA_DATA__;
// Each entry is [zoneNumber, MultiPolygon coordinates] - see MapService.EnsureZoneJsonLoaded.
var cqZoneData = __CQZONE_DATA__;
var ituZoneData = __ITUZONE_DATA__;

function escapeHtml(text) {
  if (!text) return '';
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/""/g, '&quot;');
}

// Park and SOTA summit markers, drawn on one shared canvas instead of one
// DOM icon each. A state has ~850 parks and ~700 summits; as individual icons
// every zoom repositioned 1,500+ elements, which on a slow laptop - with
// quick zooms/drags - piled up until the tab locked. Same look as the old
// icons: parks are a plain filled circle (matching the real pota.app map)
// with a white check mark when worked; summits are a brown peak with a snow
// cap, drawn with its base on the summit's location. Fixed pixel sizes, so
// they don't scale with map zoom.
var MapGlyphMarker = L.CircleMarker.extend({
  options: { glyph: 'circle', check: false },

  _updatePath: function () {
    var renderer = this._renderer;
    if (!renderer._drawing || this._empty()) return;
    var ctx = renderer._ctx, p = this._point;
    ctx.save();
    if (this.options.glyph === 'peak') {
      ctx.beginPath();
      ctx.moveTo(p.x, p.y - 17);
      ctx.lineTo(p.x + 9, p.y);
      ctx.lineTo(p.x - 9, p.y);
      ctx.closePath();
      ctx.fillStyle = '#8B4513';
      ctx.fill();
      ctx.lineWidth = 1.3;
      ctx.lineJoin = 'round';
      ctx.strokeStyle = '#3a2312';
      ctx.stroke();
      ctx.beginPath();
      ctx.moveTo(p.x, p.y - 17);
      ctx.lineTo(p.x + 3.5, p.y - 10);
      ctx.lineTo(p.x - 3.5, p.y - 10);
      ctx.closePath();
      ctx.globalAlpha = 0.85;
      ctx.fillStyle = '#ffffff';
      ctx.fill();
    } else {
      var r = this._radius;
      ctx.beginPath();
      ctx.arc(p.x, p.y, r, 0, Math.PI * 2);
      ctx.fillStyle = this.options.fillColor;
      ctx.fill();
      ctx.lineWidth = 1.2;
      ctx.strokeStyle = '#333333';
      ctx.stroke();
      if (this.options.check && r >= 5) {
        // Same check mark as the old 14px icon (M4 7.3 l2 2 4.3-5 around its
        // center at 7,7), scaled to this radius.
        var s = r / 6;
        ctx.beginPath();
        ctx.moveTo(p.x - 3 * s, p.y + 0.3 * s);
        ctx.lineTo(p.x - 1 * s, p.y + 2.3 * s);
        ctx.lineTo(p.x + 3.3 * s, p.y - 2.7 * s);
        ctx.lineWidth = 1.8 * s;
        ctx.lineCap = 'round';
        ctx.lineJoin = 'round';
        ctx.strokeStyle = '#ffffff';
        ctx.stroke();
      }
    }
    ctx.restore();
  },

  // The peak sits above its point, not centered on it - tell the canvas
  // which area to repaint, and which area counts as a click on it.
  _updateBounds: function () {
    if (this.options.glyph !== 'peak') return L.CircleMarker.prototype._updateBounds.call(this);
    var p = this._point;
    this._pxBounds = new L.Bounds(p.subtract([11, 19]), p.add([11, 2]));
  },

  _containsPoint: function (point) {
    if (this.options.glyph !== 'peak') return L.CircleMarker.prototype._containsPoint.call(this, point);
    var p = this._point;
    return point.x >= p.x - 10 && point.x <= p.x + 10 && point.y >= p.y - 18 && point.y <= p.y + 2;
  }
});

var ParkColorWorked = '#2E8B22';
var ParkColorBoatOnly = '#FF6700';
var ParkColorNotWorked = '#FFD500';

function buildPopupHtml(p) {
  var link = 'https://pota.app/#/park/' + encodeURIComponent(p.reference);
  var html = '<div class=""pota-popup"" style=""min-width:220px;"">';
  html += '<div style=""margin-bottom:4px;""><a href=""' + link + '"" target=""_blank"" rel=""noopener"">' +
          escapeHtml(p.reference) + ' - ' + escapeHtml(p.name) + '</a></div>';

  if (p.elevationFeet !== null && p.elevationFeet !== undefined) {
    html += '<div>Elevation: ' + Math.round(p.elevationFeet).toLocaleString() + ' ft</div>';
  }

  if (p.county) {
    html += '<div>County: ' + escapeHtml(p.county) + '</div>';
  }

  if (p.kff) {
    html += '<div>KFF: ' + escapeHtml(p.kff) + '</div>';
  }

  if (p.boatAccessOnly) {
    html += '<div class=""boat-line"">Boat access only</div>';
  }

  if (p.communityCount > 0) {
    var plural = p.communityCount === 1 ? 'time' : 'times';
    html += '<div>Activated ' + p.communityCount + ' ' + plural + ', most recently by ' +
            escapeHtml(p.communityCallsign) + ' on ' + escapeHtml(p.communityDate) + '.</div>';
  } else {
    html += '<div>No activations found on record.</div>';
  }

  if (p.myCount > 0) {
    var myPlural = p.myCount === 1 ? 'time' : 'times';
    html += '<div class=""my-line"">You have activated ' + p.myCount + ' ' + myPlural +
            ', most recently on ' + escapeHtml(p.myDate) + '.</div>';
  }

  // Add to Rove / Remove from Rove - see rove.js.
  if (window.rovePopupButtonHtml) html += rovePopupButtonHtml(p.reference);

  html += '</div>';
  return html;
}

// No outbound link here (unlike buildPopupHtml's pota.app one) - SOTA doesn't
// publish a simple per-summit URL pattern the way POTA does, so this only
// shows the summit's own published data.
function buildSotaPopupHtml(s) {
  var html = '<div class=""pota-popup"" style=""min-width:200px;"">';
  html += '<div style=""margin-bottom:4px;font-weight:bold;"">' + escapeHtml(s.reference) + ' - ' + escapeHtml(s.name) + '</div>';
  html += '<div>Elevation: ' + Math.round(s.altFeet).toLocaleString() + ' ft</div>';
  html += '<div>Points: ' + s.points + '</div>';

  if (s.activationCount > 0) {
    var plural = s.activationCount === 1 ? 'time' : 'times';
    html += '<div>Activated ' + s.activationCount + ' ' + plural;
    if (s.activationCall) html += ', most recently by ' + escapeHtml(s.activationCall);
    if (s.activationDate) html += ' on ' + escapeHtml(s.activationDate);
    html += '.</div>';
  } else {
    html += '<div>No activations found on record.</div>';
  }

  html += '</div>';
  return html;
}

// Trails are always purple, EC (Erie Canalway) stays the red it landed on
// under the old per-index palette, and every other area/park layer (PAD-US
// and friends) matches the blue already used for park reference links in
// popups (.pota-popup a) elsewhere on this page.
function getLayerColor(layer) {
  if (layer.isLine) return '#8e24aa';
  if (layer.name.toLowerCase() === 'ec') return '#e6194b';
  return '#1a5fb4';
}

// GeoJSON/BoundaryFeature order is [lon, lat] - Leaflet wants [lat, lng].
function pointToLatLng(pt) { return [pt[1], pt[0]]; }

// Detail levels for DRAWING boundaries/trails (see addBoundaryLayers):
// tolerance is in degrees (0.0002 is roughly 20 m, 0.002 roughly 200 m),
// 0 = full detail. minPartSize: while zoomed out, polygon pieces/holes
// smaller than this (degrees across) are skipped - they'd be a pixel or two
// at that zoom - but a feature's largest piece is always drawn, so no park
// disappears. Checked in order, first matching minZoom wins. Measured on
// NY: 256k points full, 152k / 91k / 62k at the coarser levels.
var BoundaryDetailLevels = [
  { minZoom: 13, tolerance: 0, minPartSize: 0 },
  { minZoom: 11, tolerance: 0.0002, minPartSize: 0 },
  { minZoom: 9, tolerance: 0.0007, minPartSize: 0.0015 },
  { minZoom: 0, tolerance: 0.002, minPartSize: 0.004 }
];

function boundaryDetailLevelForZoom(zoom) {
  // Before the map has a view (getZoom() is undefined) start at the
  // coarsest level - the first real zoomend swaps in the right one.
  if (typeof zoom !== 'number' || isNaN(zoom)) return BoundaryDetailLevels.length - 1;
  for (var i = 0; i < BoundaryDetailLevels.length; i++) {
    if (zoom >= BoundaryDetailLevels[i].minZoom) return i;
  }
  return BoundaryDetailLevels.length - 1;
}

// Douglas-Peucker simplification of a [lon, lat] point list (iterative, so
// a long trail can't overflow the call stack). Returns the original list
// when there's nothing to simplify, or when simplifying would leave fewer
// than minKeep points (a polygon ring needs at least 4, a line 2).
function simplifyPointList(points, tolerance, minKeep) {
  if (tolerance <= 0 || points.length <= minKeep) return points;
  var keep = new Uint8Array(points.length);
  keep[0] = 1;
  keep[points.length - 1] = 1;
  var tol2 = tolerance * tolerance;
  var stack = [[0, points.length - 1]];
  while (stack.length) {
    var span = stack.pop(), a = span[0], b = span[1];
    var ax = points[a][0], ay = points[a][1];
    var dx = points[b][0] - ax, dy = points[b][1] - ay, len2 = dx * dx + dy * dy;
    var maxD = -1, maxI = -1;
    for (var i = a + 1; i < b; i++) {
      var px = points[i][0] - ax, py = points[i][1] - ay;
      var t = len2 ? Math.max(0, Math.min(1, (px * dx + py * dy) / len2)) : 0;
      var ex = px - t * dx, ey = py - t * dy, d = ex * ex + ey * ey;
      if (d > maxD) { maxD = d; maxI = i; }
    }
    if (maxD > tol2) {
      keep[maxI] = 1;
      stack.push([a, maxI], [maxI, b]);
    }
  }
  var out = [];
  for (var j = 0; j < points.length; j++) if (keep[j]) out.push(points[j]);
  return out.length >= minKeep ? out : points;
}

// A feature's Leaflet lat/lng arrays at one detail level, built once and
// cached on the feature. Never touches feature.geometry itself.
function boundaryLatLngs(feature, isLine, level) {
  var cache = feature.lodLatLngs || (feature.lodLatLngs = {});
  if (cache[level]) return cache[level];
  var tolerance = BoundaryDetailLevels[level].tolerance;
  var minPartSize = BoundaryDetailLevels[level].minPartSize;
  if (isLine) {
    cache[level] = feature.geometry.map(function (part) { return simplifyPointList(part[0], tolerance, 2).map(pointToLatLng); });
    return cache[level];
  }
  var sizes = feature.geometry.map(function (part) { return ringExtent(part[0]); });
  var largest = sizes.indexOf(Math.max.apply(null, sizes));
  var parts = [];
  feature.geometry.forEach(function (part, i) {
    if (minPartSize > 0 && i !== largest && sizes[i] < minPartSize) return;
    var rings = [];
    part.forEach(function (ring, r) {
      if (r > 0 && minPartSize > 0 && ringExtent(ring) < minPartSize) return; // tiny hole
      rings.push(simplifyPointList(ring, tolerance, 4).map(pointToLatLng));
    });
    parts.push(rings);
  });
  cache[level] = parts;
  return cache[level];
}

// Width or height of a [lon, lat] ring's bounding box, whichever is larger.
function ringExtent(ring) {
  var minLon = 180, minLat = 90, maxLon = -180, maxLat = -90;
  for (var i = 0; i < ring.length; i++) {
    var p = ring[i];
    if (p[0] < minLon) minLon = p[0];
    if (p[0] > maxLon) maxLon = p[0];
    if (p[1] < minLat) minLat = p[1];
    if (p[1] > maxLat) maxLat = p[1];
  }
  return Math.max(maxLon - minLon, maxLat - minLat);
}

function buildBoundaryPopupHtml(featureName, layerName) {
  return '<div class=""pota-popup"">' + escapeHtml(featureName) +
    '<div class=""boundary-popup-layer"">' + escapeHtml(layerName) + '</div></div>';
}

function makeSwatch(color, isLine) {
  var swatch = document.createElement('span');
  swatch.className = 'layer-swatch';
  swatch.style.background = color;
  swatch.style.borderRadius = isLine ? '0' : '2px';
  return swatch;
}

// One flat checkbox row - used for every area layer, and for each trail
// once its group is expanded. Built with DOM calls (not an HTML string)
// so a park/boundary name never needs manual escaping here.
function buildLeafRow(entry, map) {
  var row = document.createElement('label');
  row.className = 'layer-tree-row';

  var checkbox = document.createElement('input');
  checkbox.type = 'checkbox';
  checkbox.addEventListener('change', function () {
    setLayerWanted(entry.group, checkbox.checked);
    if (entry.onChange) entry.onChange();
  });
  entry.checkbox = checkbox;

  row.appendChild(checkbox);
  row.appendChild(makeSwatch(entry.color, entry.isLine));
  row.appendChild(document.createTextNode(' ' + entry.name + ' '));

  // A trail entry is always exactly one route, so its own count is always
  // ""(1)"" - not informative the way an area layer's unit count is, so it's
  // left off leaf rows for trails (the group header shows the count that
  // actually means something: how many trails there are).
  if (!entry.isLine) {
    var count = document.createElement('span');
    count.className = 'layer-count';
    count.textContent = '(' + entry.count + ')';
    row.appendChild(count);
  }

  return row;
}

// The ""Trails"" node: a header (expand arrow + a select-all checkbox showing
// checked/unchecked/indeterminate depending on its children) plus a
// collapsible list of the individual trails. The arrow sits outside the
// header's <label> specifically so clicking it only expands/collapses -
// were it inside the label, a native click-to-toggle-the-checkbox would
// fire too.
function buildTrailGroup(trailEntries, map) {
  var wrapper = document.createElement('div');
  wrapper.className = 'layer-tree-group';

  var header = document.createElement('div');
  header.className = 'layer-tree-row layer-tree-group-header';

  var toggle = document.createElement('span');
  toggle.className = 'layer-tree-toggle';
  toggle.textContent = '▶';

  var innerLabel = document.createElement('label');
  innerLabel.className = 'layer-tree-row';

  var groupCheckbox = document.createElement('input');
  groupCheckbox.type = 'checkbox';

  var groupName = document.createElement('span');
  groupName.textContent = ' Trails ';

  var groupCount = document.createElement('span');
  groupCount.className = 'layer-count';
  groupCount.textContent = '(' + trailEntries.length + ')';

  innerLabel.appendChild(groupCheckbox);
  innerLabel.appendChild(makeSwatch('#8e24aa', true));
  innerLabel.appendChild(groupName);
  innerLabel.appendChild(groupCount);
  header.appendChild(toggle);
  header.appendChild(innerLabel);

  var children = document.createElement('div');
  children.className = 'layer-tree-children';
  children.hidden = true;

  function updateGroupCheckboxState() {
    var checkedCount = trailEntries.filter(function (t) { return t.checkbox.checked; }).length;
    groupCheckbox.checked = checkedCount > 0;
    groupCheckbox.indeterminate = checkedCount > 0 && checkedCount < trailEntries.length;
  }

  trailEntries.forEach(function (entry) {
    entry.onChange = updateGroupCheckboxState;
    children.appendChild(buildLeafRow(entry, map));
  });

  // Ticking the group box sets every trail to the same new state - the
  // standard ""select all"" pattern. Programmatically setting .checked
  // doesn't fire 'change' on its own, so each affected child's is
  // dispatched by hand to actually add/remove its layer.
  groupCheckbox.addEventListener('change', function () {
    var newState = groupCheckbox.checked;
    groupCheckbox.indeterminate = false;
    trailEntries.forEach(function (entry) {
      if (entry.checkbox.checked !== newState) {
        entry.checkbox.checked = newState;
        entry.checkbox.dispatchEvent(new Event('change'));
      }
    });
  });

  toggle.addEventListener('click', function () {
    children.hidden = !children.hidden;
    toggle.textContent = children.hidden ? '▶' : '▼';
  });

  wrapper.appendChild(header);
  wrapper.appendChild(children);
  return wrapper;
}

// Builds every layer's Leaflet objects (area boundaries as filled
// multi-polygons, trails as multi-line routes - see
// MapBoundaryLayerDto/MapGeoFeatureDto), then fills in the sidebar's own
// boundary section: area layers as flat checkboxes, trails grouped under
// one collapsible ""Trails"" node whose own checkbox turns all of them
// on/off together, expandable for individual control. Everything starts
// unchecked/off, same as potamap.us's own default, so a state's full
// boundary data can be embedded without slowing down or cluttering the
// initial view - nothing is drawn until its box (or the group box) is
// checked.
function addBoundaryLayers(map) {
  var areaEntries = [];
  var trailEntries = [];

  // All boundaries and trails share one canvas instead of one SVG element
  // each. With every layer on, a state can have ~1,800 shapes / ~370,000
  // points; as SVG, every zoom or drag while zoomed out made the browser
  // rebuild and repaint all of those elements, which on a slow laptop
  // piled up faster than it could keep up (layers lagging the base map,
  // then the whole tab freezing). One canvas redraws the same shapes far
  // more cheaply. tolerance keeps thin lines clickable for their popups.
  // Own pane just under Leaflet's overlayPane (z-index 400), so the canvas
  // never covers the GPS dot, its accuracy circle or the measure line.
  map.createPane('boundaryPane').style.zIndex = 350;
  var boundaryRenderer = L.canvas({ pane: 'boundaryPane', padding: 0.5, tolerance: 4 });

  // Drawn shapes use a pre-simplified copy of each boundary/trail while
  // zoomed out, and full detail only from street level in. Leaflet
  // re-projects every point of every shape at the end of every zoom, so
  // at state/county zoom full detail (~370,000 points for NY) cost about a
  // second per zoom on a slow laptop and made the layers visibly lag the
  // base map. Only the drawing changes: the in-park / near-trail GPS checks
  // below still use the full-detail feature.geometry.
  var boundaryShapes = [];
  var boundaryDetailLevel = boundaryDetailLevelForZoom(map.getZoom());

  boundaryLayers.forEach(function (layer) {
    var color = getLayerColor(layer);
    var group = L.layerGroup();

    layer.features.forEach(function (feature) {
      var popupHtml = buildBoundaryPopupHtml(feature.name, layer.name);
      var shape;
      var latLngs = boundaryLatLngs(feature, layer.isLine, boundaryDetailLevel);

      if (layer.isLine) {
        // Multi-line: one array entry per disconnected segment. Bold: thick,
        // fully-opaque stroke so a trail reads clearly against the tiles.
        shape = L.polyline(latLngs, { color: color, weight: 5, opacity: 1, renderer: boundaryRenderer, smoothFactor: 1.5 });
      } else {
        // Multi-polygon-with-holes: part[0] is a piece's outer ring, any
        // further rings in that part are holes cut out of it.
        // smoothFactor: Leaflet also re-simplifies each shape for the current
        // zoom (in screen pixels). 2 is still well under what's visible.
        shape = L.polygon(latLngs, { color: color, weight: 1.5, fillColor: color, fillOpacity: 0.18, renderer: boundaryRenderer, smoothFactor: 2 });
      }

      shape.bindPopup(popupHtml).addTo(group);
      boundaryShapes.push({ shape: shape, feature: feature, isLine: layer.isLine });
    });

    var entry = { name: layer.name, color: color, isLine: layer.isLine, group: group, count: layer.features.length };
    (layer.isLine ? trailEntries : areaEntries).push(entry);
    gateDetailLayer(group, false);
  });

  // Swap every shape to the matching detail level - only when a zoom
  // actually crosses a level boundary, not on every zoom.
  map.on('zoomend', function () {
    var level = boundaryDetailLevelForZoom(map.getZoom());
    if (level === boundaryDetailLevel) return;
    boundaryDetailLevel = level;
    boundaryShapes.forEach(function (s) { s.shape.setLatLngs(boundaryLatLngs(s.feature, s.isLine, level)); });
  });

  // Left hidden (see its markup) when there's nothing to show - a state
  // with no matched boundary/trail data shouldn't leave an empty,
  // pointlessly-bordered section sitting in the sidebar.
  if (areaEntries.length === 0 && trailEntries.length === 0) return;

  var section = document.getElementById('boundaryLayerSection');
  section.hidden = false;

  var heading = document.createElement('div');
  heading.className = 'sidebar-heading';
  heading.textContent = 'Park Boundaries';
  section.appendChild(heading);

  areaEntries.forEach(function (entry) { section.appendChild(buildLeafRow(entry, map)); });
  if (trailEntries.length > 0) section.appendChild(buildTrailGroup(trailEntries, map));
}

// Leaflet's own +/- zoom buttons would sit top-left, under the coordinate
// readout (coord-box) - they're added bottom-right instead, stacked above the
// recenter button (see RecenterControl below).
var map = L.map('map', { zoomControl: false });

// ---- Detail layers (park boundaries, trails, SOTA summits). Zoomed out to a
// whole state they'd mean thousands of shapes that every zoom or drag has to
// re-project and redraw; on a slow laptop, quick zooms/drags at that level
// piled up faster than it could draw them until the tab locked up. So they
// only appear from DetailMinZoom in. Sticky on the way back out: once shown
// they stay until the zoom drops below DetailHideBelowZoom, so zooming back
// and forth around the threshold doesn't swap everything in and out each
// time. A layer's checkbox still records what the user asked for (wanted);
// the layer is on the map only while details are shown as well.
var DetailMinZoom = 9;
var DetailHideBelowZoom = 8;
var detailShown = null; // unknown until the map has a view
var detailLayers = [];
var detailShownListeners = [];

// Returns true when detailShown changed.
function updateDetailShown() {
  var zoom = map.getZoom();
  if (typeof zoom !== 'number' || isNaN(zoom)) return false;
  var was = detailShown;
  if (detailShown === null) detailShown = zoom >= DetailMinZoom;
  else if (!detailShown && zoom >= DetailMinZoom) detailShown = true;
  else if (detailShown && zoom < DetailHideBelowZoom) detailShown = false;
  return detailShown !== was;
}

function syncDetailLayer(gate) {
  var show = gate.wanted && detailShown === true;
  if (show && !map.hasLayer(gate.layer)) map.addLayer(gate.layer);
  else if (!show && map.hasLayer(gate.layer)) map.removeLayer(gate.layer);
}

function gateDetailLayer(layer, wanted) {
  var gate = { layer: layer, wanted: !!wanted };
  detailLayers.push(gate);
  syncDetailLayer(gate);
  return gate;
}

// Checkbox handlers call this instead of map.addLayer/removeLayer.
function setLayerWanted(layer, wanted) {
  var found = false;
  detailLayers.forEach(function (gate) {
    if (gate.layer !== layer) return;
    found = true;
    gate.wanted = wanted;
    syncDetailLayer(gate);
  });
  if (!found) { if (wanted) map.addLayer(layer); else map.removeLayer(layer); }
  updateZoomInNote();
}

// Explains why a ticked boundary/trail/summit layer isn't showing yet.
function updateZoomInNote() {
  var note = document.getElementById('zoomInNote');
  if (!note) return;
  note.hidden = !(detailShown === false && detailLayers.some(function (gate) { return gate.wanted; }));
}

map.on('zoomend', function () {
  if (updateDetailShown()) {
    detailLayers.forEach(syncDetailLayer);
    detailShownListeners.forEach(function (fn) { fn(detailShown); });
  }
  updateZoomInNote();
});

var streetLayer = L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
  maxZoom: 19,
  attribution: '&copy; <a href=""https://www.openstreetmap.org/copyright"">OpenStreetMap</a> contributors'
});

// Esri's World Imagery - free, no API key/account needed, same as the
// OpenStreetMap tiles above.
var satelliteLayer = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', {
  maxZoom: 19,
  attribution: 'Tiles &copy; Esri &mdash; Source: Esri, Maxar, Earthstar Geographics, and the GIS User Community'
});

// Parks show at every zoom (canvas markers - see MapGlyphMarker): small
// while zoomed out, full size with the worked check mark once details show.
var workedLayer = L.layerGroup().addTo(map);
var notWorkedLayer = L.layerGroup().addTo(map);
var markerRenderer = L.canvas({ padding: 0.5, tolerance: 3 });
var ParkRadiusZoomedOut = 4;
var ParkRadiusDetail = 6;
// Starts unchecked/off - a state can have many summits - same convention as
// the boundary/trail layers below (addBoundaryLayers), and a detail layer too.
var sotaLayer = L.layerGroup();
gateDetailLayer(sotaLayer, false);

// Base map + overlay toggles, and (below) the sidebar show/hide button -
// plain HTML in the #sidebar panel instead of Leaflet's own
// L.control.layers, so it can live alongside the legend and the boundary
// layer checkboxes (addBoundaryLayers below) in one collapsible place
// rather than as several separate floating boxes around the map.
// No base layer is added until it's known whether this state's offline map
// is available (see below) - otherwise the online Street tiles would start
// downloading for the moment before the offline map replaced them, wasting
// data online and just failing with no signal.
var currentBaseLayer = null;
function setBaseLayer(layer) {
  if (!layer || layer === currentBaseLayer) return;
  if (currentBaseLayer) map.removeLayer(currentBaseLayer);
  map.addLayer(layer);
  currentBaseLayer = layer;
}
function useStreetIfNoBaseYet() {
  if (!currentBaseLayer) setBaseLayer(streetLayer);
}
// The offline layer is only created once the app confirms this state's
// offline map is downloaded (see below), hence the lookup at change time.
var offlineLayer = null;
function wireBaseLayerRadio(id, getLayer) {
  document.getElementById(id).addEventListener('change', function (e) {
    if (e.target.checked) setBaseLayer(getLayer());
  });
}
wireBaseLayerRadio('baseLayerStreet', function () { return streetLayer; });
wireBaseLayerRadio('baseLayerSatellite', function () { return satelliteLayer; });
wireBaseLayerRadio('baseLayerOffline', function () { return offlineLayer; });

// Offline base map: a vector map of the loaded state (OpenStreetMap data,
// styled by osm-style.js to look like the online Street layer) that the
// app's Offline Map menu downloads once and keeps. The app's local server
// serves it in pieces at /offline-map.pmtiles. Only possible when this page
// came from that server - a saved copy opened later (file://) has no server
// behind it, and just keeps the online layers exactly as before. When the
// state's map is downloaded it's picked by default, since it works with or
// without signal and matches the Street layer's look.
if (location.protocol === 'http:' && typeof protomapsL !== 'undefined' && window.osmStyle) {
  fetch('/offline-map/info', { cache: 'no-store' }).then(function (r) { return r.json(); }).then(function (info) {
    var note = document.getElementById('offlineMapNote');
    if (!info.available) {
      if (info.state) {
        note.textContent = 'No offline map for ' + info.state + ' yet - check Offline Map and click OK in the app, then click Show Map again.';
        note.hidden = false;
      }
      useStreetIfNoBaseYet();
      return;
    }
    osmStyle.ready.then(function () {
      offlineLayer = protomapsL.leafletLayer({
        url: '/offline-map.pmtiles',
        maxDataZoom: 15,
        maxZoom: 19,
        paintRules: osmStyle.paintRules,
        labelRules: osmStyle.labelRules,
        attribution: '&copy; <a href=""https://www.openstreetmap.org/copyright"">OpenStreetMap</a> contributors, <a href=""https://protomaps.com"">Protomaps</a>'
      });
      document.getElementById('baseLayerOfflineLabel').textContent = 'Offline (' + info.state + ')';
      document.getElementById('baseLayerOfflineRow').hidden = false;
      note.textContent = 'Offline covers ' + info.state + ' only - areas past the state line show gray.';
      note.hidden = false;
      if (document.getElementById('baseLayerStreet').checked) {
        document.getElementById('baseLayerOffline').checked = true;
        setBaseLayer(offlineLayer);
      }
    });
  }).catch(useStreetIfNoBaseYet); // app closed - online layers only
  // Never leave the map blank if the check is slow for some reason; a late
  // answer still switches to the offline map (Street is still checked).
  setTimeout(useStreetIfNoBaseYet, 3000);
} else {
  setBaseLayer(streetLayer);
}

function wireOverlayCheckbox(id, layers) {
  document.getElementById(id).addEventListener('change', function (e) {
    layers.forEach(function (layer) { setLayerWanted(layer, e.target.checked); });
  });
}
wireOverlayCheckbox('overlayWorked', [workedLayer]);
wireOverlayCheckbox('overlayNotWorked', [notWorkedLayer]);
wireOverlayCheckbox('overlaySota', [sotaLayer]);

var sidebarEl = document.getElementById('sidebar');
var sidebarToggleEl = document.getElementById('sidebarToggle');
// The toggle button and the sidebar itself sit at the same top-left spot -
// otherwise, whenever the sidebar is open, the button sits right on top of
// its own Layers header text. The button only needs to exist to reopen a
// closed sidebar, so keep it hidden any time the sidebar is already shown.
function updateSidebarToggleVisibility() {
  sidebarToggleEl.hidden = !sidebarEl.hidden;
}
sidebarToggleEl.addEventListener('click', function () {
  sidebarEl.hidden = !sidebarEl.hidden;
  updateSidebarToggleVisibility();
});
document.getElementById('sidebarClose').addEventListener('click', function () {
  sidebarEl.hidden = true;
  updateSidebarToggleVisibility();
});
updateSidebarToggleVisibility();

addBoundaryLayers(map);

sotaData.forEach(function (s) {
  var marker = new MapGlyphMarker([s.lat, s.lon], { renderer: markerRenderer, glyph: 'peak', radius: 9 });
  // Open above the peak (it's drawn above its point), like the old icon's popupAnchor.
  marker.bindPopup(buildSotaPopupHtml(s), { offset: [0, -10] });
  marker.addTo(sotaLayer);
});

var bounds = [];
var parkMarkers = [];
parkData.forEach(function (p) {
  // (0, 0) is what an ungeocoded park looks like here - it's out in the Gulf
  // of Guinea, nowhere near a real US park, so this only filters those out.
  if (!p.lat && !p.lon) return;
  var marker = new MapGlyphMarker([p.lat, p.lon], {
    renderer: markerRenderer, radius: ParkRadiusZoomedOut, check: p.completed,
    fillColor: p.completed ? ParkColorWorked : (p.boatAccessOnly ? ParkColorBoatOnly : ParkColorNotWorked)
  });
  // Popup built on first open rather than up front for every park.
  marker.bindPopup(function () { return buildPopupHtml(p); });
  marker.addTo(p.completed ? workedLayer : notWorkedLayer);
  parkMarkers.push(marker);
  bounds.push([p.lat, p.lon]);
});

// Small dots zoomed out, full-size circles (with the worked check mark) once
// details show - a canvas redraw, not 850 elements swapped in and out.
detailShownListeners.push(function (shown) {
  var radius = shown ? ParkRadiusDetail : ParkRadiusZoomedOut;
  parkMarkers.forEach(function (m) { m.setRadius(radius); });
});

if (bounds.length > 0) {
  map.fitBounds(bounds, { padding: [40, 40] });
} else {
  map.setView([39.8, -98.6], 4); // fallback: center of the continental US
}

// ---- Right-click context menu: ""Measure distance"" (Google Maps-style
// click-to-extend polyline with a running total) and ""What's here?""
// (reverse geocode via Nominatim, the same free OpenStreetMap service the
// tile layers above already depend on - no API key). Both are purely
// best-effort, same philosophy as the GPS features below: ""What's here?""
// needs a network connection and just falls back to showing the raw
// coordinates if that fetch fails (e.g. no signal out in the field), and
// neither feature needs the app's local server behind the page, so both
// still work in a saved, standalone file opened later.
var contextMenuEl = document.getElementById('mapContextMenu');

function hideContextMenu() {
  contextMenuEl.hidden = true;
}

function addContextMenuItem(label, onClick) {
  var item = document.createElement('div');
  item.className = 'context-menu-item';
  item.textContent = label;
  item.addEventListener('click', function () {
    hideContextMenu();
    onClick();
  });
  contextMenuEl.appendChild(item);
}

function showContextMenu(latlng, clientX, clientY) {
  contextMenuEl.innerHTML = '';
  addContextMenuItem('Measure distance', function () { startMeasuring(latlng); });
  addContextMenuItem(""What's here?"", function () { showWhatsHere(latlng); });
  if (measurePoints.length > 0) {
    addContextMenuItem('Clear measurement', clearMeasuring);
  }

  // Positioned at the click, then clamped so it can't run off the
  // right/bottom edge on a narrow window - has to be shown (off-screen)
  // first to get its real offsetWidth/Height to clamp against.
  contextMenuEl.style.left = '-1000px';
  contextMenuEl.style.top = '0';
  contextMenuEl.hidden = false;
  var menuWidth = contextMenuEl.offsetWidth;
  var menuHeight = contextMenuEl.offsetHeight;
  contextMenuEl.style.left = Math.max(0, Math.min(clientX, window.innerWidth - menuWidth - 4)) + 'px';
  contextMenuEl.style.top = Math.max(0, Math.min(clientY, window.innerHeight - menuHeight - 4)) + 'px';
}

map.on('contextmenu', function (e) {
  e.originalEvent.preventDefault();
  showContextMenu(e.latlng, e.originalEvent.clientX, e.originalEvent.clientY);
});

// A left click anywhere on the map background both dismisses an open
// context menu and, while measuring, places the next point - see
// startMeasuring/addMeasurePoint below. Clicks on markers/boundary shapes
// never reach here: Leaflet already stops those from bubbling up to the
// map's own click handler, which is what lets a park popup still open
// normally mid-measurement instead of also dropping a stray point on it.
map.on('click', function (e) {
  hideContextMenu();
  if (measureActive) addMeasurePoint(e.latlng);
});
map.on('movestart zoomstart', hideContextMenu);
document.addEventListener('keydown', function (e) {
  if (e.key === 'Escape') { hideContextMenu(); clearMeasuring(); }
});

function showWhatsHere(latlng) {
  var coordsLine = latlng.lat.toFixed(5) + ', ' + latlng.lng.toFixed(5);
  var popup = L.popup()
    .setLatLng(latlng)
    .setContent('<div class=""pota-popup"">Looking up address&hellip;</div>')
    .openOn(map);

  fetch('https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat=' + latlng.lat + '&lon=' + latlng.lng + '&zoom=18&addressdetails=1')
    .then(function (resp) { if (!resp.ok) throw new Error('reverse geocode failed'); return resp.json(); })
    .then(function (data) {
      var address = data && data.display_name ? escapeHtml(data.display_name) : 'No address found for this location.';
      popup.setContent('<div class=""pota-popup"" style=""min-width:200px;"">' +
        '<div style=""margin-bottom:4px;"">' + address + '</div>' +
        '<div style=""opacity:0.7;font-size:11px;"">' + coordsLine + '</div></div>');
    })
    .catch(function () {
      popup.setContent('<div class=""pota-popup"" style=""min-width:200px;"">' +
        '<div>Address lookup unavailable (no signal?)</div>' +
        '<div style=""opacity:0.7;font-size:11px;"">' + coordsLine + '</div></div>');
    });
}

// ---- Measuring tool: click to drop points, with a running total shown
// both in the bottom panel and as a live tentative-distance tooltip that
// follows the cursor ahead of the next click (the ""rubber band"" line) -
// the same interaction Google Maps' own ""Measure distance"" tool uses.
// Distances use Leaflet's own LatLng.distanceTo (great-circle, via the
// Haversine formula) rather than the flat-degree approximation the
// park-boundary checks above use - those trade accuracy for speed over
// many repeated checks, but this tool only computes a handful of
// distances per click, so the more accurate spherical formula costs
// nothing here.
var measureActive = false;
var measurePoints = [];
var measureMarkers = [];
var measureLine = null;
var measureRubberBand = null;
var measureTotalMeters = 0;

var measureVertexIcon = L.divIcon({
  html: '<div style=""width:10px;height:10px;border-radius:50%;background:#ffffff;border:2px solid #1a73e8;box-shadow:0 0 2px rgba(0,0,0,0.6);""></div>',
  className: '',
  iconSize: [10, 10],
  iconAnchor: [5, 5]
});

var measurePanelEl = document.getElementById('measurePanel');
var measureDistanceTextEl = document.getElementById('measureDistanceText');

// Feet under half a mile, miles beyond that - mirrors Google Maps' own
// US-unit switchover rather than always showing one unit.
function formatDistance(meters) {
  var feet = meters * 3.28084;
  if (feet < 2640) return Math.round(feet).toLocaleString() + ' ft';
  return (feet / 5280).toFixed(2) + ' mi';
}

function recalcMeasureTotal() {
  measureTotalMeters = 0;
  for (var i = 1; i < measurePoints.length; i++) {
    measureTotalMeters += measurePoints[i - 1].distanceTo(measurePoints[i]);
  }
  measureDistanceTextEl.textContent = 'Distance: ' + formatDistance(measureTotalMeters);
}

function rebuildMeasureLine() {
  if (measureLine) { map.removeLayer(measureLine); measureLine = null; }
  if (measurePoints.length > 1) {
    measureLine = L.polyline(measurePoints, { color: '#1a73e8', weight: 3 }).addTo(map);
  }
}

function discardRubberBand() {
  if (measureRubberBand) { map.removeLayer(measureRubberBand); measureRubberBand = null; }
}

function removeMeasureVertexAt(index) {
  measurePoints.splice(index, 1);
  var marker = measureMarkers.splice(index, 1)[0];
  map.removeLayer(marker);
  rebuildMeasureLine();
  recalcMeasureTotal();
  discardRubberBand();
  if (measurePoints.length === 0) measurePanelEl.hidden = true;
}

// Clicking a placed point removes it (and reconnects its neighbors) -
// matches Google Maps' own measuring tool. stopPropagation keeps that
// click from also bubbling up to the map's own click handler, which would
// otherwise immediately re-add a point at the same spot.
function addMeasureVertexMarker(latlng) {
  var marker = L.marker(latlng, { icon: measureVertexIcon }).addTo(map);
  marker.on('click', function (e) {
    L.DomEvent.stopPropagation(e);
    var idx = measureMarkers.indexOf(marker);
    if (idx !== -1) removeMeasureVertexAt(idx);
  });
  measureMarkers.push(marker);
}

function startMeasuring(latlng) {
  clearMeasuring();
  measureActive = true;
  L.DomUtil.addClass(map.getContainer(), 'measuring-cursor');
  measurePoints.push(latlng);
  addMeasureVertexMarker(latlng);
  recalcMeasureTotal();
  measurePanelEl.hidden = false;
}

function addMeasurePoint(latlng) {
  measurePoints.push(latlng);
  addMeasureVertexMarker(latlng);
  rebuildMeasureLine();
  recalcMeasureTotal();
  discardRubberBand(); // next mousemove rebuilds it from the new last point
}

function clearMeasuring() {
  measureActive = false;
  L.DomUtil.removeClass(map.getContainer(), 'measuring-cursor');
  measurePoints = [];
  measureMarkers.forEach(function (m) { map.removeLayer(m); });
  measureMarkers = [];
  if (measureLine) { map.removeLayer(measureLine); measureLine = null; }
  discardRubberBand();
  measureTotalMeters = 0;
  measurePanelEl.hidden = true;
}

map.on('mousemove', function (e) {
  if (!measureActive || measurePoints.length === 0) return;
  var last = measurePoints[measurePoints.length - 1];
  var tentative = measureTotalMeters + last.distanceTo(e.latlng);
  if (!measureRubberBand) {
    measureRubberBand = L.polyline([last, e.latlng], { color: '#1a73e8', weight: 2, dashArray: '6,6' })
      .addTo(map)
      .bindTooltip('', { direction: 'right', offset: [12, 0] });
  }
  measureRubberBand.setLatLngs([last, e.latlng]);
  measureRubberBand.setTooltipContent(formatDistance(tentative));
  measureRubberBand.openTooltip(e.latlng);
});

document.getElementById('measureCloseBtn').addEventListener('click', clearMeasuring);

// Live ""you are here"" marker from whichever source the app's GPS dropdown
// has selected, plus a bottom-right button that flies back to it on demand:
//   'serial'  - a GPS receiver on a COM port, read by the app itself and
//               polled here via the local server's /gps endpoint (pollGps)
//   'browser' - Windows / Browser Location: this page's own
//               navigator.geolocation, which on Windows comes from the
//               Windows location service (Wi-Fi/cell-tower lookups on a PC
//               without GPS hardware - can be miles off)
//   'off'     - no location
// All purely best-effort - with no fix, a denied permission or the receiver
// unplugged, the map works exactly as it does without them.
var youMarker = null;
var youAccuracyCircle = null;
var youLocatedOnce = false;
var recenterButton = null;

// Where the position comes from:
//  - the map as served by the app: whatever the app's GPS dropdown picks
//    (/gps says which, and carries the COM-port receiver's fixes)
//  - a saved copy (Layers > Save a copy of this map, opened from a file):
//    the GPS receiver first, asked from the app at embeddedGps.url while
//    PAT is running on this PC with a receiver that has a fix; otherwise
//    Windows / browser location, which Chrome does give a page opened from
//    a file once allowed (tested 2026-10-08).
var servedByApp = location.protocol === 'http:';
var gpsAppUrl = servedByApp ? '/gps' : (window.embeddedGps ? embeddedGps.url : null);
var gpsAvailable = servedByApp || !!(navigator.geolocation);

// Bottom-right readout showing whether fresh position fixes are actually
// arriving and how far off they're expected to be - the ""you are here"" dot
// alone can't distinguish a stalled source from one that's just not moving
// because you aren't. Ticks once a second so a fix that stops arriving shows
// growing as ""updated Xs ago"" instead of looking current.
var gpsStatusEl = document.getElementById('gpsStatus');
var lastFixTimestamp = null;
var lastFixAccuracy = null;

// From the last /gps answer: the selected source (null until the first
// answer), the COM port and the app's status message for it (e.g. ""COM3 not
// available"") while it has no fix, and whether /gps itself stopped
// answering (the app was closed).
var gpsSource = null;
var gpsPortName = '';
var gpsStatusMessage = '';
var gpsServerLost = false;

// Windows / Browser Location only: the browser's permission for this page to
// use location, shown alongside the fix info so a blocked or never-answered
// permission is visible on sight instead of looking identical to ""hasn't got
// a fix yet"". Queried once up front and kept live via onchange - Chrome
// remembers a per-site grant/block permanently once set (the fixed map
// server port keeps this page the same site every time - see
// Form1.MapServerPort).
var geoPermissionState = null;
if (gpsAvailable && navigator.permissions && navigator.permissions.query) {
  navigator.permissions.query({ name: 'geolocation' }).then(function (result) {
    geoPermissionState = result.state;
    updateGpsStatusText();
    result.onchange = function () {
      geoPermissionState = result.state;
      updateGpsStatusText();
    };
  }).catch(function () { /* Permissions API unsupported here - just omitted below. */ });
}
// The browser's own error for the last failed request (permission denied,
// timed out, ...), cleared by the next successful fix.
var browserLocationError = '';

function updateGpsStatusText() {
  if (!gpsStatusEl) return;

  if (!gpsAvailable) {
    gpsStatusEl.hidden = false;
    gpsStatusEl.textContent = 'GPS: this browser gives a saved map no location - use Show Map in the app for live tracking.';
    return;
  }

  if (gpsServerLost) {
    gpsStatusEl.hidden = false;
    gpsStatusEl.textContent = 'GPS: lost connection to the app - is it still running?';
    return;
  }

  if (gpsSource === null) return;

  gpsStatusEl.hidden = false;
  if (gpsSource === 'off') {
    gpsStatusEl.textContent = 'GPS: off (pick a location source in the GPS dropdown in the app)';
    return;
  }

  var label = gpsSource === 'serial' ? 'GPS (' + gpsPortName + ')' : gpsSource === 'direct' ? 'GPS receiver' : 'Windows location';
  var parts = [];
  if (gpsSource === 'browser' && geoPermissionState && geoPermissionState !== 'granted') parts.push('permission: ' + geoPermissionState);
  if (lastFixTimestamp !== null) {
    var seconds = Math.max(0, Math.round((Date.now() - lastFixTimestamp) / 1000));
    var ago = seconds < 60 ? (seconds + 's ago') : (Math.round(seconds / 60) + 'm ago');
    parts.push('±' + Math.round(lastFixAccuracy) + ' m, updated ' + ago);
  } else if (gpsSource === 'serial') {
    parts.push(gpsStatusMessage || 'waiting for a fix...');
    if (followMode) parts.push('will keep following when the fix returns');
  } else {
    parts.push(browserLocationError || 'waiting for a location...');
  }
  gpsStatusEl.textContent = label + ': ' + parts.join(', ');
}
setInterval(updateGpsStatusText, 1000);

// Set when the button is clicked before any fix has arrived yet - resolved
// (flown to) by the next onLocationFound.
var pendingRecenter = false;

// ""Follow me"" mode: once you've pressed the recenter button, later fixes
// keep the map centered on the dot automatically (like a normal nav app),
// right up until you drag or zoom the map yourself - at which point it
// backs off instead of constantly fighting you while you're looking
// around, matching the same ""don't yank the view around"" philosophy
// onLocationFound already applies elsewhere. programmaticMove distinguishes
// ""the view moved because this file just called flyTo/panTo/fitBounds""
// from ""the view moved because you dragged or zoomed it"" - without it,
// recenterOnMe's own flyTo (which can include a zoom change, to at least
// zoom level 14) would immediately flip followMode back off the moment it
// started, since Leaflet fires the same zoomstart event either way.
var followMode = false;
var programmaticMove = false;
// Every follow-mode change goes through here so the recenter button always
// shows the current state - green while following, normal when not.
function setFollowMode(on) {
  followMode = on;
  if (recenterButton) recenterButton.classList.toggle('following', on);
}
// When the +/- zoom buttons were last clicked (see zoomControl below) - a
// zoom starting shortly after one is the button's, not the user's own
// wheel/pinch zoom, so it mustn't cancel follow mode.
var lastZoomButtonClickMs = 0;
var ZoomButtonGraceMs = 1500;
map.on('moveend', function () { programmaticMove = false; });
map.on('dragstart', function () {
  if (!programmaticMove) setFollowMode(false);
});
map.on('zoomstart', function () {
  if (!programmaticMove && Date.now() - lastZoomButtonClickMs > ZoomButtonGraceMs) setFollowMode(false);
});

function recenterOnMe() {
  if (!gpsAvailable || gpsSource === 'off') {
    updateGpsStatusText(); // surfaces the ""not available in a saved file"" / ""GPS off"" explanation right away
    return;
  }
  setFollowMode(true);
  if (youMarker) {
    programmaticMove = true;
    map.flyTo(youMarker.getLatLng(), Math.max(map.getZoom(), 14));
  } else {
    pendingRecenter = true;
    recenterButton.classList.add('waiting');
    recenterButton.title = 'Waiting for your location...';
  }
}

var RecenterControl = L.Control.extend({
  options: { position: 'bottomright' },
  onAdd: function () {
    var container = L.DomUtil.create('div', 'leaflet-bar recenter-control');
    L.DomEvent.disableClickPropagation(container);

    var button = L.DomUtil.create('a', '', container);
    button.href = '#';
    button.title = 'Center on my location';
    button.innerHTML =
      '<svg width=""18"" height=""18"" viewBox=""0 0 18 18"" xmlns=""http://www.w3.org/2000/svg"">' +
      '<circle cx=""9"" cy=""9"" r=""2.5"" fill=""currentColor""/>' +
      '<path d=""M9 1v3M9 14v3M1 9h3M14 9h3"" stroke=""currentColor"" stroke-width=""1.6"" stroke-linecap=""round""/>' +
      '</svg>';

    L.DomEvent.on(button, 'click', function (e) {
      L.DomEvent.preventDefault(e);
      recenterOnMe();
    });

    recenterButton = button;
    return container;
  }
});

map.addControl(new RecenterControl());
// Added after the recenter button on purpose: Leaflet stacks each new
// bottom-corner control ABOVE the ones already there, so this puts the
// +/- buttons directly on top of it.
var zoomControl = L.control.zoom({ position: 'bottomright', zoomInTitle: 'Zoom in', zoomOutTitle: 'Zoom out' }).addTo(map);

// A +/- click must NOT cancel follow mode the way a drag or wheel/pinch zoom
// does - while following, the view is centered on the dot, so these just
// zoom in/out around you. Recorded in the capture phase, i.e. before
// Leaflet's own click handler starts the zoom and fires zoomstart. A time
// window rather than the programmaticMove flag: with several quick clicks,
// the previous zoom's moveend cleared that flag before the next click's
// zoomstart, so rapid -/- clicks still switched follow mode off. The window
// also can't get stuck on the way a flag could when a click at the min/max
// zoom does nothing.
zoomControl.getContainer().addEventListener('click', function (e) {
  var link = e.target.closest ? e.target.closest('a') : null;
  if (link && (link.classList.contains('leaflet-control-zoom-in') || link.classList.contains('leaflet-control-zoom-out'))) {
    lastZoomButtonClickMs = Date.now();
  }
}, true);

// ---- Live ""am I in/near a park right now"" check, per POTA's own
// activation rules - not the same question the Xfer's grid column
// answers (whether a PARK'S OWN reported point overlaps another park's
// boundary); this is about where YOU actually are right now, tested
// against the real boundary/trail geometry already embedded above
// (boundaryLayers) - exactly what POTA requires an activator to verify
// before claiming an overlap or a trail activation, rather than trusting
// a park's single reported coordinate. Runs on every GPS fix regardless
// of which sidebar layer checkboxes are currently ticked - this is about
// accuracy, not what's currently drawn.
var infoBarEl = document.getElementById('infoBar');
var TrailCheckToleranceKm = 0.03048; // 100 ft (30.5 m) - POTA's own trail-activation rule, same constant FerLookupService.TrailToleranceKm uses server-side.
var KmPerDegreeLatCheck = 111.32;

// Same ray-casting parity test as FerLookupService.PointInRing/CountyLookupService.PointInRing.
function pointInRingCheck(lon, lat, ring) {
  var inside = false;
  for (var i = 0, j = ring.length - 1; i < ring.length; j = i++) {
    var xi = ring[i][0], yi = ring[i][1];
    var xj = ring[j][0], yj = ring[j][1];
    if (((yi > lat) !== (yj > lat)) && (lon < (xj - xi) * (lat - yi) / (yj - yi) + xi)) inside = !inside;
  }
  return inside;
}

function isPointInAreaFeature(lon, lat, feature) {
  if (lon < feature.minLon || lon > feature.maxLon || lat < feature.minLat || lat > feature.maxLat) return false;
  for (var p = 0; p < feature.geometry.length; p++) {
    var ringsContaining = 0;
    var part = feature.geometry[p];
    for (var r = 0; r < part.length; r++) {
      if (pointInRingCheck(lon, lat, part[r])) ringsContaining++;
    }
    if (ringsContaining % 2 === 1) return true;
  }
  return false;
}

// ---- Top-left readout: Maidenhead grid square, CQ/ITU zone, and lat/long
// for wherever the mouse currently is - no click needed. Mirrors the
// mouse-tracking readout at zone-check.eu. CQ/ITU zones aren't a simple
// formula (their boundaries partly follow political borders, not just
// meridians/parallels - see cqZoneData/ituZoneData above), so this reuses
// the same point-in-polygon test as the park/boundary layers above,
// against zone boundary polygons simplified enough to stay a small,
// offline-friendly embed while still picking the right zone in practice.
function buildZoneFeatures(zoneData) {
  return zoneData.map(function (entry) {
    var zone = entry[0], geometry = entry[1];
    var minLon = 180, minLat = 90, maxLon = -180, maxLat = -90;
    geometry.forEach(function (part) {
      part.forEach(function (ring) {
        ring.forEach(function (pt) {
          if (pt[0] < minLon) minLon = pt[0];
          if (pt[0] > maxLon) maxLon = pt[0];
          if (pt[1] < minLat) minLat = pt[1];
          if (pt[1] > maxLat) maxLat = pt[1];
        });
      });
    });
    return { zone: zone, geometry: geometry, minLon: minLon, minLat: minLat, maxLon: maxLon, maxLat: maxLat };
  });
}

var cqZoneFeatures = buildZoneFeatures(cqZoneData);
var ituZoneFeatures = buildZoneFeatures(ituZoneData);

function findZone(lon, lat, zoneFeatures) {
  for (var i = 0; i < zoneFeatures.length; i++) {
    if (isPointInAreaFeature(lon, lat, zoneFeatures[i])) return zoneFeatures[i].zone;
  }
  return null;
}

// Standard 6-character Maidenhead locator (e.g. ""EM12ab"").
function toGridSquare(lat, lon) {
  lon += 180;
  lat += 90;
  var A = 65; // 'A'.charCodeAt(0)
  var field = String.fromCharCode(A + Math.floor(lon / 20)) + String.fromCharCode(A + Math.floor(lat / 10));
  var lonRem = lon - Math.floor(lon / 20) * 20;
  var latRem = lat - Math.floor(lat / 10) * 10;
  var square = Math.floor(lonRem / 2).toString() + Math.floor(latRem).toString();
  lonRem -= Math.floor(lonRem / 2) * 2;
  latRem -= Math.floor(latRem);
  var subSquare = String.fromCharCode(97 + Math.floor(lonRem / (2 / 24))) + String.fromCharCode(97 + Math.floor(latRem / (1 / 24)));
  return field + square + subSquare;
}

var coordGridEl = document.getElementById('coordGrid');
var coordItuEl = document.getElementById('coordItu');
var coordCqEl = document.getElementById('coordCq');
var coordLatEl = document.getElementById('coordLat');
var coordLongEl = document.getElementById('coordLong');
var coordUpdatePending = false;
var coordLatLng = null;

function updateCoordBox() {
  coordUpdatePending = false;
  if (!coordLatLng) return;
  var lat = coordLatLng.lat, lon = coordLatLng.lng;
  coordGridEl.textContent = toGridSquare(lat, lon);
  var ituZone = findZone(lon, lat, ituZoneFeatures);
  var cqZone = findZone(lon, lat, cqZoneFeatures);
  coordItuEl.textContent = ituZone === null ? '–' : ituZone;
  coordCqEl.textContent = cqZone === null ? '–' : cqZone;
  coordLatEl.textContent = lat.toFixed(4) + '°';
  coordLongEl.textContent = lon.toFixed(4) + '°';
}

map.on('mousemove', function (e) {
  coordLatLng = e.latlng;
  if (!coordUpdatePending) {
    coordUpdatePending = true;
    requestAnimationFrame(updateCoordBox);
  }
});

// Same locally-scaled-degree distance math as FerLookupService.PointToSegmentDistanceKm.
function distancePointToSegmentKm(px, py, ax, ay, bx, by, lonScale) {
  var axs = ax * lonScale, bxs = bx * lonScale, pxs = px * lonScale;
  var dx = bxs - axs, dy = by - ay;
  var t = (dx === 0 && dy === 0) ? 0 : Math.max(0, Math.min(1, ((pxs - axs) * dx + (py - ay) * dy) / (dx * dx + dy * dy)));
  var cx = axs + t * dx, cy = ay + t * dy;
  var ex = pxs - cx, ey = py - cy;
  return Math.sqrt(ex * ex + ey * ey) * KmPerDegreeLatCheck;
}

function isPointNearTrailFeature(lon, lat, feature) {
  var latPad = TrailCheckToleranceKm / KmPerDegreeLatCheck;
  var lonScale = Math.max(0.1, Math.cos(lat * Math.PI / 180));
  var lonPad = TrailCheckToleranceKm / (KmPerDegreeLatCheck * lonScale);
  if (lon < feature.minLon - lonPad || lon > feature.maxLon + lonPad ||
      lat < feature.minLat - latPad || lat > feature.maxLat + latPad) return false;

  for (var p = 0; p < feature.geometry.length; p++) {
    var line = feature.geometry[p][0]; // a trail feature's ""ring"" is really just its own point sequence - see MapGeoFeatureDto.
    for (var i = 0; i < line.length - 1; i++) {
      var d = distancePointToSegmentKm(lon, lat, line[i][0], line[i][1], line[i + 1][0], line[i + 1][1], lonScale);
      if (d <= TrailCheckToleranceKm) return true;
    }
  }
  return false;
}

function normalizeNameWords(name) {
  if (!name) return [];
  var collapsed = name.toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim();
  return collapsed.length === 0 ? [] : collapsed.split(' ').sort();
}

// Best-effort cross-reference from a boundary/trail feature's own name to
// its POTA reference, for display only - same word-set-equality idea as
// FerLookupService.NormalizeName server-side (minus its safe-subset
// fallback, which isn't worth the false-positive risk here: showing the
// raw boundary/trail name on a near-miss is still useful, unlike
// server-side where a wrong Xfer match matters more). Returns null - and
// callers fall back to the raw feature name - when nothing matches, or
// more than one park name matches equally (ambiguous).
function findParkByFeatureName(featureName) {
  var target = normalizeNameWords(featureName).join(' ');
  if (!target) return null;
  var match = null;
  for (var i = 0; i < parkData.length; i++) {
    if (normalizeNameWords(parkData[i].name).join(' ') === target) {
      if (match) return null;
      match = parkData[i];
    }
  }
  return match;
}

function formatFeatureLabel(featureName) {
  var park = findParkByFeatureName(featureName);
  return park ? (park.reference + ' - ' + featureName) : featureName;
}

function checkNearbyParks(lat, lon) {
  if (!infoBarEl) return;

  var insideNames = {};
  var nearTrailNames = {};
  boundaryLayers.forEach(function (layer) {
    layer.features.forEach(function (feature) {
      if (layer.isLine) {
        if (isPointNearTrailFeature(lon, lat, feature)) nearTrailNames[feature.name] = true;
      } else if (isPointInAreaFeature(lon, lat, feature)) {
        insideNames[feature.name] = true;
      }
    });
  });

  var lines = [];
  Object.keys(insideNames).forEach(function (n) { lines.push('Inside: ' + formatFeatureLabel(n)); });
  Object.keys(nearTrailNames).forEach(function (n) { lines.push('Within 100 ft of: ' + formatFeatureLabel(n)); });

  infoBarEl.hidden = false;
  if (lines.length === 0) {
    infoBarEl.classList.remove('info-bar-active');
    infoBarEl.textContent = 'Not currently within any known park boundary or trail.';
  } else {
    infoBarEl.classList.add('info-bar-active');
    infoBarEl.innerHTML = lines.map(escapeHtml).join('<br>');
  }
}

// Fixes that land within this distance of where the dot is already drawn
// only refresh the status readout (""updated Xs ago"", accuracy) - they skip
// moving the dot, the in-park check against every boundary/trail, and any
// follow-me panning. A receiver reporting once a second while you sit at
// your operating spot jitters by a meter or two, so without this the map
// would redo all of that work every second for no visible change.
// Compared against the last DRAWN position, not the last fix, so slow
// drift still adds up and moves the dot once it passes the threshold.
var MinMoveMeters = 5;

function onLocationFound(e) {
  clearLocatePending();
  browserLocationError = '';
  lastFixTimestamp = e.timestamp || Date.now();
  lastFixAccuracy = e.accuracy;
  updateGpsStatusText();

  if (youMarker && !pendingRecenter && map.distance(youMarker.getLatLng(), e.latlng) < MinMoveMeters) return;

  checkNearbyParks(e.latlng.lat, e.latlng.lng);

  var popupHtml = 'Your location (±' + Math.round(e.accuracy) + ' m)';

  if (!youMarker) {
    youMarker = L.circleMarker(e.latlng, {
      radius: 8, weight: 3, color: '#ffffff', opacity: 1,
      fillColor: '#1a73e8', fillOpacity: 1
    }).addTo(map).bindPopup(popupHtml);
    youAccuracyCircle = L.circle(e.latlng, {
      radius: e.accuracy, weight: 1, color: '#1a73e8', fillColor: '#1a73e8', fillOpacity: 0.1
    }).addTo(map);
  } else {
    youMarker.setLatLng(e.latlng).setPopupContent(popupHtml);
    youAccuracyCircle.setLatLng(e.latlng).setRadius(e.accuracy);
  }

  if (pendingRecenter) {
    pendingRecenter = false;
    recenterButton.classList.remove('waiting');
    recenterButton.title = 'Center on my location';
    programmaticMove = true;
    map.flyTo(e.latlng, Math.max(map.getZoom(), 14));
  } else if (followMode) {
    // Keep the dot centered on every later fix too, not just the one right
    // after pressing the button - panTo (not flyTo) so this never touches
    // zoom, only position: a real user zoom is still the only thing that
    // should change zoom, and panTo alone never fires zoomstart, so it
    // can't spuriously trip the followMode-cancelling listener above.
    programmaticMove = true;
    map.panTo(e.latlng);
  }

  // Only nudge the view on the very first fix, and only if it's not already
  // visible - e.g. the parks loaded are for a state you're not currently
  // standing in. After that, leave the view alone (beyond followMode above)
  // so a later GPS update never yanks the map out from under you while
  // you're panning or zooming it.
  if (!youLocatedOnce) {
    youLocatedOnce = true;
    if (!map.getBounds().contains(e.latlng)) {
      programmaticMove = true;
      map.fitBounds(map.getBounds().extend(e.latlng), { padding: [40, 40] });
    }
  }
}

// A failed Windows / Browser Location request: not worth a popup, but shown
// in the gpsStatus readout so a permission-denied / unavailable / timed-out
// failure is visible instead of looking like the map is just waiting on a
// first fix forever. A pending recenter-button click shouldn't be left
// waiting forever on a fix that isn't coming either.
function onLocationError(e) {
  clearLocatePending();
  browserLocationError = e && e.message ? e.message : 'location unavailable';
  if (recenterButton) {
    recenterButton.classList.remove('waiting');
    recenterButton.title = 'Center on my location';
  }
  pendingRecenter = false;
  updateGpsStatusText();
}

// A browser request still outstanding when the GPS dropdown switches away
// from Windows / Browser Location is dropped rather than plotted over the
// new source.
map.on('locationfound', function (e) {
  if (gpsSource === 'browser') onLocationFound(e); else clearLocatePending();
});
map.on('locationerror', function (e) {
  if (gpsSource === 'browser') onLocationError(e); else clearLocatePending();
});

// Clears the ""you are here"" dot and everything derived from it, so
// switching sources (or turning GPS off, or a lost fix) never leaves a stale
// position on screen. keepFollow is for a lost fix only: a receiver dropping
// out for a while (tunnel, trees, a loose cable) mustn't cancel follow mode,
// or the map would stop following when the fix comes back. The app keeps
// retrying the receiver and pollGps keeps asking every second, so following
// simply resumes on the next fix.
function clearYouMarker(keepFollow) {
  if (youMarker) { map.removeLayer(youMarker); youMarker = null; }
  if (youAccuracyCircle) { map.removeLayer(youAccuracyCircle); youAccuracyCircle = null; }
  lastFixTimestamp = null;
  lastFixAccuracy = null;
  browserLocationError = '';
  if (!keepFollow) setFollowMode(false);
  pendingRecenter = false;
  if (recenterButton) {
    recenterButton.classList.remove('waiting');
    recenterButton.title = 'Center on my location';
  }
  if (infoBarEl) infoBarEl.hidden = true;
}

var lastFixSeq = null;

function setGpsSource(source, port) {
  if (source === gpsSource && port === gpsPortName) return;
  gpsSource = source;
  gpsPortName = port;
  lastFixSeq = null;
  clearYouMarker();
  if (source === 'browser') requestBrowserLocation(); // don't wait up to 10 s for the first one
}

function pollGps() {
  // A saved copy reading the receiver itself (direct-gps.js) - first choice
  // whenever it has a fresh fix.
  var direct = window.directGps ? directGps.latest() : null;
  if (direct) {
    setGpsSource('direct', '');
    if (direct.seq !== lastFixSeq) {
      lastFixSeq = direct.seq;
      onLocationFound({ latlng: L.latLng(direct.lat, direct.lon), accuracy: direct.accuracy, timestamp: direct.time });
    }
    updateGpsStatusText();
    return;
  }
  // A saved copy with no way to reach the app: Windows / browser location.
  if (!gpsAppUrl) { setGpsSource('browser', ''); updateGpsStatusText(); return; }
  fetch(gpsAppUrl, { cache: 'no-store' }).then(function (r) {
    if (!r.ok) throw new Error('HTTP ' + r.status);
    return r.json();
  }).then(function (data) {
    gpsServerLost = false;
    var source = data.mode === 'serial' && data.serial ? 'serial' : (data.mode === 'browser' ? 'browser' : 'off');
    // A saved copy uses the receiver only while it has a fix, and Windows
    // location the rest of the time - including when the app's dropdown is
    // off - since nobody there is choosing a source.
    if (!servedByApp && !(source === 'serial' && data.serial.fix)) source = 'browser';
    setGpsSource(source, source === 'serial' ? data.serial.port : '');

    if (source === 'serial') {
      gpsStatusMessage = data.serial.status || '';
      var fix = data.serial.fix;
      if (!fix) {
        // Receiver lost its fix (or was unplugged) - drop the dot rather
        // than leave it showing a position that's no longer current, but
        // stay in follow mode so the map picks up again on the next fix.
        if (lastFixTimestamp !== null) clearYouMarker(true);
      } else if (fix.seq !== lastFixSeq) {
        lastFixSeq = fix.seq;
        onLocationFound({
          latlng: L.latLng(fix.lat, fix.lon),
          accuracy: fix.accuracy,
          timestamp: Date.now() - fix.ageMs
        });
      }
    }
    updateGpsStatusText();
  }).catch(function () {
    if (servedByApp) {
      gpsServerLost = true;
    } else {
      setGpsSource('browser', ''); // a saved copy, and the app isn't running
    }
    updateGpsStatusText();
  });
}

// ---- Windows / Browser Location polling (only while that source is
// selected). One one-shot request at a time, every 10 seconds.
//
// Deliberately NOT a persistent map.locate({watch:true}) subscription
// alongside this poll - confirmed directly, by comparing this page's own
// repeated one-shot requests against the same test on a page with no
// competing geolocation activity: with a concurrent watchPosition
// subscription running, requests here took dramatically longer. Exactly one
// request outstanding at a time is what actually resolved reliably.
//
// locatePending guards against firing a new request while one's still
// outstanding - confirmed necessary: a real fix here goes through a
// several-second Wi-Fi-based lookup, and overlapping requests started timing
// each other out (""Timeout expired"") instead of resolving.
//
// locatePendingTimeoutId is a second, independent safety net: confirmed that
// a locate() call can occasionally call back neither handler at all, even
// with an explicit timeout. Without this, that one hung request would leave
// locatePending stuck true forever and stop the readout updating for good;
// with it, a hung request degrades to ""one skipped update"".
var locatePending = false;
var locatePendingTimeoutId = null;

function clearLocatePending() {
  locatePending = false;
  if (locatePendingTimeoutId !== null) {
    clearTimeout(locatePendingTimeoutId);
    locatePendingTimeoutId = null;
  }
}

function requestBrowserLocation() {
  if (gpsSource !== 'browser' || locatePending) return;
  locatePending = true;
  locatePendingTimeoutId = setTimeout(clearLocatePending, 15000);
  map.locate({ setView: false, enableHighAccuracy: true, maximumAge: 0, timeout: 10000 });
}

if (gpsAvailable) {
  pollGps();
  setInterval(pollGps, 1000);
  setInterval(requestBrowserLocation, 10000);
} else {
  updateGpsStatusText();
}
</script>
<script>
__DIRECT_GPS_JS__
</script>
<script>
// Filled in only in a saved copy of the map (MapService.ToStandaloneHtml):
// the app's rove plans and their cached routes, since a saved copy has no
// app behind it - see rove.js.
var embeddedRove = null;
</script>
<script>
__ROVE_JS__
</script>
</body>
</html>";
    }
}
