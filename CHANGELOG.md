# TrailStumbler Changelog

## master
### ✨ Features and improvements
- _...Add new stuff here..._
- **Choose which points the map shows.** **Settings → Points on the map** lists the kinds of imported points, each with a box to show or hide it on every points layer at once. Fuel, parking and trailheads, food, lodging, camping, scenic places, restrooms, ATV clubs and starred places are shown at first; town services and other trail map points are there to turn on. Waypoints of your own (GPX, GeoJSON) always show.
- **Point labels can show the description.** The map's **Labels** button, which used to switch labels on and off, now picks what points are labelled with: their name, their description or nothing, as the web map's Point Label did. The choice is remembered.

### 🐞 Bug fixes
- _...Add new stuff here..._
- **Imported points no longer pile up as black dots.** About a third of the points on the TrailIntel maps were a large black dot drawn over everything. The ones whose names say what they are (garages, ATMs and banks, fire and police stations, post offices, town halls, pharmacies, laundromats, shops) get their own icons, which Settings can show; the rest are a small faded dot, hidden unless Settings shows them, and points with no name are left out. Re-import a map to sort its points. Where icons would overlap, the ones with an icon show first and the rest appear as you zoom in.
- **The turn instructions no longer cover the map's buttons.** While navigating, the map starts below the instruction panel, so the 3D terrain, zoom and compass buttons and the scale bar stay in view.

## 0.2.0
### ✨ Features and improvements
- **A scale bar on the map.** Top left, a bar of a round distance in kilometres or metres, which changes as you zoom.
- **Leave points out of a recorded track.** ✎ on a recording (Layers → My recordings) opens it on the map with its points: tap one point, then another, and **Exclude** the stretch between (a road driven by mistake), or **Include** it again. The points are kept, but the track is drawn, measured and exported without them, a gap where they were rather than a line across it
- **Export several layers at once.** **Export** on the Layers page saves all your layers, or those shown on the map, to one GeoJSON, KML or GPX file: GeoJSON tags each feature with its layer, KML gives each layer a folder, and GPX has the points as waypoints and the trails and recordings as tracks (recordings with their times and elevations)
- **Record a track from the map.** The round button under the map's buttons, top right, starts and stops recording; while recording it shows how long and how far beside it. Each recording is saved as its own track in Layers → My recordings, to show, hide, zoom to or export as GPX. Recording was there all along (with crash recovery and Android's background service), but nothing in the app started it
- **Route by ATV or UTV.** The route planner's travel choices add **Hybrid ATV (offline)** and **Hybrid UTV (offline)** beside hybrid moto: the same trails-and-roads routing, with the trails each may use. A motorcycle keeps off trails that bar motorcycles, an ATV (50 in) may use those, a UTV (60 in) keeps off 50-inch trails too; none takes a closed trail
- **Garmin trail maps show what each trail is.** On TrailIntel's ATV and snowmobile maps and GPS Trailmasters' ATV map, each trail is described as GPS Trailmasters' KMZ describes it ("Corridor", "UTV/ATV 60 Inches, No Motorcycles", "Closed ATV Trail"; on the snowmobile map "Provincial / ITS Trail", "Regional Trail", "Danger / Caution") and drawn in its colour (green 60-inch trails, blue corridors, red 50-inch trails, black access roads, grey closed trails). Parking and trailheads get the parking marker, scenic places the scenic marker. See docs/garmin-import.md
- **Routes keep off trails they may not use.** Closed trails are left out of every route, and trails barring motorcycles out of motorcycle routes, when the trail's data says so: a known Garmin map's trail kind, a KML/GPX description such as GPS Trailmasters' ("Closed ATV Trail", "No Motorcycles", "50 Inches"), or GeoJSON properties `closed`, `no_motorcycles`, `max_width_in`. Any other trail is open to all
- **Routing updated to MaplibreNative.Routing 0.2.2.** The online "Auto (road)" choice takes highways like any road; the motorcycle and trail choices still avoid them
- **Routing updated to MaplibreNative.Routing 0.2.1.** Offline routes keep to the main road (no hops through parking lots beside it, as on NH 16 through Pinkham Notch), with no false turns or names flapping at map-tile edges; "turn right to stay on" a road that bends at a junction; travel times are driving times; the first instruction isn't cut off by the next, and the voice no longer cuts itself off
- **Routing updated to MaplibreNative.Routing 0.2.0.** Start follows the route you chose at once (it no longer plans it again); tap the instruction bar while navigating for the rest of the directions; the bar shows the next turn and the distance to it; a simulated drive follows a reroute (round a closure); long trips load the tiles along the way rather than the box round it. Stopping a simulated drive of a route not yet started goes back to the planned route
- **The app opens on the map.** Map is the first tab, then Layers and Settings
- **Plan a route, then Start it.** **Plan Route** finds and draws the routes to choose from, and nothing follows the GPS yet: before, it navigated at once, so a ride planned away from A was rerouted straight away from wherever the phone was. **Start** follows the chosen route with the GPS, and **Simulate** drives it; either way the route sheet tucks itself away for the map
- **Closures: routes go round a closed trail.** **Mark closed** in the route sheet, then tap the map where it's closed (it snaps to a trail within 50 m); while navigating, **Closed ahead** marks the route 60 m ahead of you. Either way a new route is found from where you are, round it, by road if need be, and every later reroute keeps going round it. Closures show as red dots and are kept for the next route planned until cleared (✕) or Clear
- **Navigation follows you and talks.** The map follows you while navigating, turned to the road, tilted, and zoomed to the next turn, framed between the instruction panel and the route sheet. Move the map and it waits: **Recenter** follows again, and **Overview** shows the rest of the route. Turn instructions are spoken (**Voice** in the route sheet turns it off, and it's remembered)
- **Rerouting.** Leave the route and a new one is planned from where you are; the route sheet says so ("Off the route: finding a new one…", "New route from here"), and when you've arrived
- **Simulated drive.** **Simulate** in the route sheet drives the route at four times real speed, with the voice, the camera and rerouting as on a real ride, to try a route before setting off. A track being recorded keeps the real GPS meanwhile
- **A Garmin map's background map can be imported too.** Importing a Garmin file that carries one (TrailIntel's export with the background, GPS Trailmasters' New England file) asks whether to include it. The trails are imported either way; the background (roads, water, towns) gets layers of its own, "(background roads)", "(background points)" and "(background areas)", hidden until turned on and not used for routing, as it can be millions of features
- **Routing updated to MaplibreNative.Routing 0.1.0** (from 0.0.13). The offline hybrid modes now route over your trails and the map's roads as one network: a trip can start on a road, take a trail, leave it by road round a gap and rejoin it, and a trail end within 50 m of a road is joined to it. Hybrid moto prefers the trails, taking rural roads before bigger ones only where it has to. Road names come in whole, not cut at tile edges
- **3D terrain button on the map** — a button above the zoom controls turns 3D terrain on and off, as in VistumblerMAUI. On the WifiDB Color Relief styles it uses the style's own elevation data; on a style without any, such as WifiDB OSM or a custom style, the app adds WifiDB's terrain. Tilt the map to see the hills: drag up or down with two fingers on Android.

### 🐞 Bug fixes
- **Zoom to track works on Windows.** The map zoomed nowhere: on Windows the map is taken down while another tab shows and rebuilt on return, and the zoom was tried before it was back. It now waits for the map
- **The map ends above the route sheet.** It ran on under the sheet's header, which hid its bottom edge and the attribution button; the open sheet still slides up over the map
- **Garmin POIs and trails have their names.** Most points of interest had garbled names or another place's (a bitcoin ATM among the restaurants), and the main trails of TrailIntel's maps had none: a POI's name is in its POI record, and a routable trail's in its road record, which weren't read
- **The Windows app opens where you left it**, at the size you left it and maximized if it was. It always opened at the default place and size, which could be partly under the taskbar. It's kept within the screen: a place under the taskbar, or on a monitor no longer there, is moved into view
- **The route sheet's routing log keeps a line per step.** Each tile downloaded added a line ("Downloading tiles (1254/9180)…", then 1255, ...); a step's progress now updates its own line
- **Recording a track and navigating at once both keep the GPS.** They share the app's one GPS listener, and each turned it off when it ended: stopping or clearing a route while recording left the recording without positions, and stopping a recording while navigating left navigation without them
- **Garmin .img import draws the trails where they are.** Lines on routable maps (TrailIntel's, GPS Trailmasters') came out as zig-zags and starbursts around the real trail, and some as long straight lines across the map: each point's routing bit was read as two bits in the wrong place, and a step too big for its bits (written as escapes) was read as a plain negative number. Both are now read as mkgmap writes them. Checked against GPS Trailmasters' GPX of the same 2023 release: every New Hampshire trail now has the Garmin line within 2.4 m (only 41% within 25 m before); TrailIntel's current New England map matches 95% of those 2023 trails within 10 m
- **A combined Garmin map file imports** (a gmapsupp-style file with several maps, like GPS Trailmasters' New England one): its directory is read where the header says, not assumed at 0x600, where it found nothing. The maker's background map inside is left out (from the file's MPS: one described as a background map or basemap, or with mkgmap's default "OSM street map" beside a map that isn't): GPS Trailmasters' "Background Map" alone was 7 million points and a million lines, and TrailIntel's took six and a half minutes
- **The route planner sheet fits on the screen** — its rows were fixed at 36 pixels while the buttons and the travel-mode picker take at least 44, so Place A/B, Plan Route, Clear and the picker were cut off at the bottom. The sheet now sizes to what it holds, slides up and down rather than squeezing, and scrolls past 60% of the screen (a long routing log no longer runs off it). Its header shows "Route" and what's set ("Set B", "Tap the map to place A") while it's tucked away; tap or swipe it to open and close. **Set A** / **Set B** tuck the sheet away so the map can be tapped, and open it again once the pin is placed. It follows the dark theme, as the tap-to-inspect popup now does, and that popup sits above the sheet's header rather than behind it
- **Map renderer updated to MapLibreNative.Maui.Handlers 5.0.0-pre.5** (from 4.2.1). What changes on the map:
  - The GPS control is two buttons. The top one cycles Off → Show → Follow, and the bottom one cycles the map's direction: free, north up, or turning with your heading. Dragging the map while following drops back to Show.
  - The compass button resets the tilt as well as the direction.
  - An attribution you open stays open for 10 seconds, and it wraps at the map's width.
  - On Android, tapping with two fingers zooms out, and a crash when many map tiles load at once (panning quickly, or leaving the app) is fixed.
  - The Android app's map library is built for 16 KB memory pages, which Google Play requires, and 32-bit ARM phones get the map too.


## 0.1.3
### 🐞 Bug fixes
- **Attribution banner no longer re-expands on every runtime source refresh** — updated the map renderer to MapLibreNative.Maui.Handlers 4.2.1, whose attribution handling only rewrites and re-expands the banner when the attribution content actually changes. Previously any periodically-updated runtime source (e.g. a live GeoJSON overlay) made the banner pop open on every update; runtime-added sources on Windows also now surface their attribution.


## 0.1.2
### ✨ Features and improvements
- **Android touch gestures on the map** — via the renderer bump to MapLibreNative.Maui.Handlers 4.2.0, two-finger pinch-zoom, rotate and tilt now work on Android (previously only the on-screen zoom/rotate buttons did).

### 🐞 Bug fixes
- **Android: the map no longer crashes the app on open** — the renderer bump to 4.2.0 fixes a native stack-overflow crash on Android when the map is first shown.
- **Android: several map rendering bugs fixed** — also via 4.2.0: polygon fills no longer show a checkerboard pattern and tiles no longer show white seams; the map no longer stretches/blanks after rotating the device; panning tracks the finger correctly; and tiles now refresh when zooming into detailed (color-relief/hillshade/vector) styles instead of staying stuck on lower-zoom content.


## 0.1.1
### ✨ Features and improvements
- **Updated map renderer to MapLibreNative.Maui.Handlers 4.1.3** (from a 4.1.0 dev build) — now consumed as the released package from nuget.org.
- **Self-contained Windows releases, now with ARM64** — Windows releases ship a native `win-arm64` build alongside `win-x64`, and both are self-contained: the .NET runtime and the Windows App SDK runtime are bundled, so users don't need to install anything first. ARM64 devices run the app and its native map engine (`mln-cabi.dll`) natively instead of under x64 emulation.

### 🐞 Bug fixes
- **MAUI Windows: double-tapping the nav/GPS/d-pad overlay buttons no longer leaks through to the map** — via the renderer bump to 4.1.2, the overlay buttons now handle `DoubleTapped` (previously only `Tapped`), so the second click of a fast double-click no longer bubbles past the button and zooms/pans the map behind it.


## 0.1.0
### ✨ Features and improvements
- Import GeoJSON, KML, KMZ, and GPX files as named map layers with per-layer visibility toggle and colour picker
- GPS Trail Masters KMZ support: trail-class colours from KML `<Style>` defs, POI category icons (parking, fuel, food, lodging, camping, scenic, restroom, atv_club), and clean descriptions
- MapLibre map rendering with data-driven line colour (`["case",["has","stroke"],...]`), symbol icons, and fill layers
- Tap-to-inspect popup showing feature name, layer, category, and description
- Zoom-to-layer button navigates the map to the layer's bounding box
- GPS track recording: live yellow line on map, save as layer, GPX/KML export via system file picker
- Hybrid offline routing via `MaplibreNative.Routing` plugin (ATV/motorcycle, bicycle, walking) using MapLibre vector tile road graph + A★ trail graph
- Alternative route support: choose 1–3 route attempts; shortest selected by default, others shown as selectable cards in the route sheet
- Route plan pull-up sheet: A/B waypoint placement, profile picker, Plan/Stop/Clear actions, highway warning
- Android foreground service for background track recording with wake lock
