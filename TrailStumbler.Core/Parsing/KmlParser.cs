/*
 * KmlParser â€” C# port of Mapbox's togeojson KML conversion logic (simplified),
 * via the Java port in geojson-mapper (KmlToGeoJson.java).
 * Original project: https://github.com/mapbox/togeojson (BSD-2-Clause)
 * Attribution: Mapbox and contributors; see https://github.com/mapbox/togeojson/blob/master/LICENSE
 *
 * Additions over the port: shared <Style>/<StyleMap> resolution so KML styling
 * survives import as GeoJSON simplestyle properties â€” LineStyle color/width become
 * "stroke"/"stroke-width" (KML colors are AABBGGRR!), IconStyle icon hrefs become
 * "icon" plus a "category" derived from the icon filename (GPS Trail Masters uses
 * parking/fuel/food/lodging/camping/star icons). Mixed-geometry placemarks emit one
 * feature per geometry (instead of a GeometryCollection) so geometry-family
 * splitting stays uniform.
 */
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using TrailStumbler.Core.Models;

namespace TrailStumbler.Core.Parsing;

public class KmlParser : IGisParser
{
    private sealed record KmlStyle(string? Stroke, double? StrokeWidth, string? IconHref);

    public List<ParsedFeature> Parse(Stream stream)
    {
        XDocument doc;
        try { doc = XDocument.Load(stream); }
        catch (Exception ex) { throw new FormatException("Not a readable KML file.", ex); }

        var root = doc.Root ?? throw new FormatException("Empty KML document.");
        var styles = CollectStyles(root);

        var results = new List<ParsedFeature>();
        foreach (var placemark in Descendants(root, "Placemark"))
        {
            try { PlacemarkToFeatures(placemark, styles, results); }
            catch { /* ignore per-feature failures, like the original */ }
        }
        return results;
    }

    // â”€â”€ Styles â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static Dictionary<string, KmlStyle> CollectStyles(XElement root)
    {
        var styles = new Dictionary<string, KmlStyle>();
        foreach (var style in Descendants(root, "Style"))
        {
            var id = style.Attribute("id")?.Value;
            if (string.IsNullOrEmpty(id)) continue;
            styles[id] = ParseStyle(style);
        }
        // StyleMap: resolve to its "normal" (or first) pair's styleUrl.
        foreach (var map in Descendants(root, "StyleMap"))
        {
            var id = map.Attribute("id")?.Value;
            if (string.IsNullOrEmpty(id)) continue;
            var pairs = Elements(map, "Pair").ToList();
            var normal = pairs.FirstOrDefault(p =>
                string.Equals(Value(p, "key"), "normal", StringComparison.OrdinalIgnoreCase)) ?? pairs.FirstOrDefault();
            var target = Value(normal, "styleUrl")?.TrimStart('#');
            if (target is not null && styles.TryGetValue(target, out var resolved))
                styles[id] = resolved;
        }
        return styles;
    }

    private static KmlStyle ParseStyle(XElement style)
    {
        string? stroke = null;
        double? width = null;
        string? iconHref = null;

        var lineStyle = FirstDescendant(style, "LineStyle");
        if (lineStyle is not null)
        {
            stroke = KmlColorToHex(Value(lineStyle, "color"));
            if (double.TryParse(Value(lineStyle, "width"), NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
                width = w;
        }

        var iconStyle = FirstDescendant(style, "IconStyle");
        var href = iconStyle is not null ? Value(FirstDescendant(iconStyle, "Icon"), "href") : null;
        if (!string.IsNullOrWhiteSpace(href)) iconHref = href;

        return new KmlStyle(stroke, width, iconHref);
    }

    /// <summary>KML colors are AABBGGRR hex; convert to #RRGGBB.</summary>
    internal static string? KmlColorToHex(string? kmlColor)
    {
        if (kmlColor is null) return null;
        var s = kmlColor.Trim();
        if (s.Length != 8 || !s.All(Uri.IsHexDigit)) return null;
        var bb = s.Substring(2, 2);
        var gg = s.Substring(4, 2);
        var rr = s.Substring(6, 2);
        return $"#{rr}{gg}{bb}".ToUpperInvariant();
    }

    /// <summary>POI category from the icon filename: ".../symbols/parking.png" â†’ "parking".</summary>
    internal static string? CategoryFromIconHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        try
        {
            var file = href.Split('/', '\\').Last();
            var dot = file.LastIndexOf('.');
            var name = (dot > 0 ? file[..dot] : file).Trim().ToLowerInvariant();
            return name.Length > 0 ? name : null;
        }
        catch { return null; }
    }

    // â”€â”€ Placemarks â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void PlacemarkToFeatures(XElement placemark, Dictionary<string, KmlStyle> styles, List<ParsedFeature> results)
    {
        var geometries = new List<(string Type, string CoordsJson, List<string>? Times, Action<ParsedFeature> ExtendBbox)>();
        CollectGeometries(placemark, geometries);
        if (geometries.Count == 0) return;

        var name = Value(placemark, "name") ?? "";
        var rawDesc = Value(placemark, "description") ?? "";

        // Resolve the placemark's style: styleUrl reference, else inline <Style>.
        KmlStyle? style = null;
        var styleUrl = Value(placemark, "styleUrl")?.TrimStart('#');
        if (styleUrl is not null) styles.TryGetValue(styleUrl, out style);
        if (style is null)
        {
            var inline = Elements(placemark, "Style").FirstOrDefault();
            if (inline is not null) style = ParseStyle(inline);
        }

        var extended = ExtractExtendedData(placemark);

        foreach (var (type, coordsJson, times, extendBbox) in geometries)
        {
            var pf = new ParsedFeature
            {
                GeometryType = type,
                CoordinatesJson = coordsJson,
                Name = name,
                Description = DescriptionCleaner.CleanForDisplay(rawDesc),
            };
            extendBbox(pf);
            if (pf.Bbox.IsEmpty) continue;

            bool isLine = type is "LineString" or "MultiLineString";
            bool isPoint = type is "Point" or "MultiPoint";
            pf.PropertiesJson = BuildProps(name, DescriptionCleaner.CleanProperty(rawDesc), extended,
                stroke: isLine ? style?.Stroke : null,
                strokeWidth: isLine ? style?.StrokeWidth : null,
                iconHref: isPoint ? style?.IconHref : null,
                coordTimes: times);
            results.Add(pf);
        }
    }

    private static void CollectGeometries(
        XElement el,
        List<(string, string, List<string>?, Action<ParsedFeature>)> results)
    {
        foreach (var child in el.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "MultiGeometry":
                    CollectGeometries(child, results);
                    break;

                case "Point":
                {
                    var c = ParseCoordTuple(Value(child, "coordinates"));
                    if (c is not null)
                        results.Add(("Point", TupleJson(c), null, pf => pf.Bbox.Extend(c[0], c[1])));
                    break;
                }
                case "LineString":
                {
                    var arr = ParseCoordList(Value(child, "coordinates"));
                    if (arr.Count >= 2)
                        results.Add(("LineString", ListJson(arr), null,
                            pf => { foreach (var p in arr) pf.Bbox.Extend(p[0], p[1]); }));
                    break;
                }
                case "Polygon":
                {
                    var rings = new List<List<double[]>>();
                    foreach (var ring in Descendants(child, "LinearRing"))
                    {
                        var arr = ParseCoordList(Value(ring, "coordinates"));
                        if (arr.Count >= 4) rings.Add(arr);
                    }
                    if (rings.Count > 0)
                    {
                        var json = "[" + string.Join(",", rings.Select(ListJson)) + "]";
                        results.Add(("Polygon", json, null,
                            pf => { foreach (var p in rings[0]) pf.Bbox.Extend(p[0], p[1]); }));
                    }
                    break;
                }
                case "Track": // covers gx:Track â€” local-name matching
                {
                    var pts = Elements(child, "coord")
                        .Select(c => ParseSpaceTuple(c.Value))
                        .Where(c => c is not null)
                        .Select(c => c!)
                        .ToList();
                    if (pts.Count >= 2)
                    {
                        var times = Elements(child, "when").Select(w => w.Value.Trim()).ToList();
                        results.Add(("LineString", ListJson(pts), times.Count > 0 ? times : null,
                            pf => { foreach (var p in pts) pf.Bbox.Extend(p[0], p[1]); }));
                    }
                    break;
                }
                // Folders/Documents nested inside a Placemark don't happen; geometry
                // containers other than the above are ignored.
            }
        }
    }

    private static List<(string Key, string Val)> ExtractExtendedData(XElement placemark)
    {
        var pairs = new List<(string, string)>();
        var ed = FirstDescendant(placemark, "ExtendedData");
        if (ed is null) return pairs;
        foreach (var data in Descendants(ed, "Data"))
        {
            var key = data.Attribute("name")?.Value;
            var value = Value(data, "value");
            if (!string.IsNullOrEmpty(key) && value is not null)
                pairs.Add((key, value));
        }
        return pairs;
    }

    private static string BuildProps(
        string name, string desc, List<(string Key, string Val)> extended,
        string? stroke, double? strokeWidth, string? iconHref, List<string>? coordTimes)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            if (name.Length > 0) w.WriteString("name", name);
            if (desc.Length > 0) w.WriteString("description", desc);
            if (stroke is not null) w.WriteString("stroke", stroke);
            if (strokeWidth.HasValue) w.WriteNumber("stroke-width", strokeWidth.Value);
            if (iconHref is not null)
            {
                w.WriteString("icon", iconHref);
                var category = CategoryFromIconHref(iconHref);
                if (category is not null) w.WriteString("category", category);
            }
            foreach (var (key, val) in extended)
                w.WriteString(key, val);
            if (coordTimes is not null)
            {
                w.WriteStartArray("coordTimes");
                foreach (var t in coordTimes) w.WriteStringValue(t);
                w.WriteEndArray();
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // â”€â”€ Coordinate text parsing â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>"lon,lat[,ele]" â†’ [lon, lat, ele?]</summary>
    private static double[]? ParseCoordTuple(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Trim().Split(',');
        if (parts.Length < 2) return null;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
            return null;
        if (parts.Length > 2 &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ele) &&
            ele != 0)
            return [lon, lat, ele];
        return [lon, lat];
    }

    /// <summary>Whitespace-separated "lon,lat[,ele]" tuples.</summary>
    private static List<double[]> ParseCoordList(string? s)
    {
        var result = new List<double[]>();
        if (string.IsNullOrWhiteSpace(s)) return result;
        foreach (var token in s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var c = ParseCoordTuple(token);
            if (c is not null) result.Add(c);
        }
        return result;
    }

    /// <summary>gx:Track coord: "lon lat [ele]" (space-separated).</summary>
    private static double[]? ParseSpaceTuple(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
            return null;
        if (parts.Length > 2 &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ele) &&
            ele != 0)
            return [lon, lat, ele];
        return [lon, lat];
    }

    private static string TupleJson(double[] c) => c.Length > 2
        ? FormattableString.Invariant($"[{c[0]},{c[1]},{c[2]}]")
        : FormattableString.Invariant($"[{c[0]},{c[1]}]");

    private static string ListJson(List<double[]> pts)
        => "[" + string.Join(",", pts.Select(TupleJson)) + "]";

    // â”€â”€ XML helpers (local-name matching; KML namespace usage varies) â”€â”€â”€â”€â”€â”€â”€â”€

    private static IEnumerable<XElement> Descendants(XElement el, string localName)
        => el.Descendants().Where(e => e.Name.LocalName == localName);

    private static IEnumerable<XElement> Elements(XElement el, string localName)
        => el.Elements().Where(e => e.Name.LocalName == localName);

    private static XElement? FirstDescendant(XElement el, string localName)
        => Descendants(el, localName).FirstOrDefault();

    private static string? Value(XElement? el, string localName)
        => el?.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim();
}
