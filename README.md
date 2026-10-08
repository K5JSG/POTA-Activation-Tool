# POTA Activation Tool

PAT (POTA Activation Tool) is a Windows desktop tool for [Parks on the Air (POTA)](https://parksontheair.com/) activators. Load every park in a state, cross-reference it against your ADIF log, and get a ready-to-use activation planning report — plus an interactive map to help plan and navigate to your next activation.

Built by Jeremy S. Gaynor, K5JSG.

## Features

- **Load all parks for a state** directly from the POTA API, complete with county, elevation, and boundary data.
- **Import an ADIF log** and automatically mark which parks you've already activated (optional for the map, which opens as soon as a state's parks are loaded).
- **2-fer / 3-fer detection ("Xfer's")** — flags parks whose boundaries overlap, or that share a National Scenic/Historic Trail, so you know which activations can count for multiple parks at once.
- **SOTA cross-reference** — shows any [Summits on the Air](https://www.sotamaps.org/) summit that falls within a park's real boundary, matched by geometry rather than a pre-built cross-reference list.
- **WWFF/KFF cross-reference**, auto-downloaded and kept up to date.
- **Boat Access Only** flag, auto-detected from POTA's own park data.
- **Export** your finished report to CSV or Excel.
- **Interactive map**:
  - Park markers color-coded by status (not worked / boat-access-only / worked), shown as small dots when zoomed out to a whole state
  - Toggleable park boundary, National Trail, and SOTA summit layers, drawn from county-level zoom in so the map stays responsive on slower laptops
  - Live GPS "follow me" tracking from a GPS receiver on a COM port or the Windows/browser location, picked from the GPS dropdown — the follow button turns green while following and keeps following through brief GPS dropouts, with zoom buttons right above it
  - Distance measuring tool and address search
  - **Rove planner** — add parks from the map, by reference or name, or add any address; drag the route line to add a stop wherever you want to go. Put the stops in order by dragging them or with Order by Distance, and see the driving route with miles, drive time and turn-by-turn directions per leg. Plans are saved and reloaded from a dropdown, and **Print Directions** gives a printable page (or PDF) of the whole trip, with turn-by-turn directions or just the list of stops. Routes are saved once looked up, so they still show with no signal
  - **Load your pota.app rove plans** — sign in with **POTA Login** in the menu bar (POTA's own sign-in page; the app never sees your password), then pick a plan under **From pota.app** in the rove planner
  - Live readout of grid square, CQ/ITU zone, and lat/long under your mouse
  - Downloadable **offline street map** per state (the Offline Map checkbox) — roads, trails, parks, wildlife areas and house numbers in the standard OpenStreetMap look, with no internet needed
  - **Save a copy of the map** to an .html file from its Layers panel, including your rove plans and their driving routes. A saved copy opens without the app: it reads a GPS receiver plugged into the PC directly (Chrome or Edge), or uses the app's GPS when it's running, or Windows location, and looks up any road routes it's missing
- **Built for the field** — parks, boundaries, trails, elevation, SOTA data, park activation history (refreshed once a day), rove plans and any driving routes already looked up, and (once downloaded) the state's street map are all kept locally, so the app and its map keep working with no signal once you've loaded a state and shown its map at least once.

## Installation

Download the latest installer from the [Releases](https://github.com/K5JSG/POTA-Activation-Tool/releases) page and run it. The app is self-contained — no separate .NET runtime install is required. Installing a new version upgrades the existing copy in place, so there's only ever one entry in Installed apps and your desktop shortcut stays where it is. Any other installed copy (including very old versions from before the rename) is removed first, and files the new version no longer uses are cleaned out; your downloaded maps and settings are kept.

PAT was called **POTA Activator Park Activations** before version 1.15.0. Installing 1.15.0 or later replaces the old copy, moves its shortcuts and program folder to the new name, and carries your saved data, plans and offline maps over.

## Building from source

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download) (Windows, with the Windows Desktop workload) and, optionally, [Inno Setup](https://jrsoftware.org/isdl.php) if you want to build the installer.

```powershell
dotnet build "POTA Activation Tool.csproj"
```

To produce a self-contained release build and installer:

```powershell
.\build.ps1 -Version <version>
```

This publishes a self-contained, single-file executable to `publish\` and, if Inno Setup is installed, builds the installer into `dist\`.

## License

GNU General Public License v3.0 — see [License.txt](License.txt).

Offline map data © [OpenStreetMap contributors](https://www.openstreetmap.org/copyright), available under the Open Database License, via the [Protomaps](https://protomaps.com) basemap. The offline map style follows [openstreetmap-carto](https://github.com/gravitystorm/openstreetmap-carto) (CC0). Driving routes in the rove planner come from the public [OSRM](https://project-osrm.org) routing server, using OpenStreetMap data. Satellite imagery, and the street map in saved copies of the map, are from [Esri](https://www.esri.com). Built-in map libraries: [Leaflet](https://leafletjs.com) (BSD-2-Clause) and [protomaps-leaflet](https://github.com/protomaps/protomaps-leaflet) (BSD-3-Clause) — their license texts are in `MapAssets/`.
