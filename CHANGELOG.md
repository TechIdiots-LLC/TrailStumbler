# TrailStumbler Changelog

## master
### ✨ Features and improvements
- _...Add new stuff here..._
- **3D terrain button on the map** — a button above the zoom controls turns 3D terrain on and off, as in VistumblerMAUI. On the WifiDB Color Relief styles it uses the style's own elevation data; on a style without any, such as WifiDB OSM or a custom style, the app adds WifiDB's terrain. Tilt the map to see the hills: drag up or down with two fingers on Android.

### 🐞 Bug fixes
- _...Add new stuff here..._
- **Garmin .img import draws the trails where they are.** Lines on routable maps (TrailIntel's, GPS Trailmasters') came out as zig-zags and starbursts around the real trail, and some as long straight lines across the map: each point's routing bit was read as two bits in the wrong place, and a step too big for its bits (written as escapes) was read as a plain negative number. Both are now read as mkgmap writes them. Checked against GPS Trailmasters' GPX of the same 2023 release: every New Hampshire trail now has the Garmin line within 2.4 m (only 41% within 25 m before); TrailIntel's current New England map matches 95% of those 2023 trails within 10 m
- **A combined Garmin map file imports** (a gmapsupp-style file with several maps, like GPS Trailmasters' New England one): its directory is read where the header says, not assumed at 0x600, where it found nothing. The maker's background map inside is left out (from the file's MPS: one described as a background map or basemap, or with mkgmap's default "OSM street map" beside a map that isn't): GPS Trailmasters' "Background Map" alone was 7 million points and a million lines, and TrailIntel's took six and a half minutes
- **The route planner sheet fits on the screen** — its rows were fixed at 36 pixels while the buttons and the travel-mode picker take at least 44, so Place A/B, Plan Route, Clear and the picker were cut off at the bottom. The sheet now sizes to what it holds, slides up and down rather than squeezing, and scrolls past 60% of the screen (a long routing log no longer runs off it). Its header shows "Route" and what's set ("Set B", "Tap the map to place A") while it's tucked away; tap or swipe it to open and close. **Set A** / **Set B** tuck the sheet away so the map can be tapped, and open it again once the pin is placed. It follows the dark theme, as the tap-to-inspect popup now does, and that popup sits above the sheet's header rather than behind it
- **Map renderer updated to MapLibreNative.Maui.Handlers 5.0.0-pre.4** (from 4.2.1). What changes on the map:
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
