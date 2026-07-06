# TrailStumbler Changelog

## master
### ✨ Features and improvements
- _...Add new stuff here..._

### 🐞 Bug fixes
- _...Add new stuff here..._


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
