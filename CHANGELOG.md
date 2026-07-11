# TrailStumbler Changelog

## master
### ✨ Features and improvements
- _...Add new stuff here..._

### 🐞 Bug fixes
- _...Add new stuff here..._


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
