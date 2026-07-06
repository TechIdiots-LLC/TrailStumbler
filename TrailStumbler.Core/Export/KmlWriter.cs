using System.Globalization;
using System.Text.Json;
using System.Xml;
using TrailStumbler.Core.Models;

namespace TrailStumbler.Core.Export;

/// <summary>
/// Writes a layer's features to a KML 2.2 document.
/// Line colors are read from the simplestyle "stroke" property when present,
/// falling back to the layer's ColorHex. KML uses AABBGGRR byte order.
/// </summary>
public static class KmlWriter
{
    private const string KmlNs = "http://www.opengis.net/kml/2.2";

    public static async Task WriteAsync(Stream stream, MapLayer layer,
        IReadOnlyList<MapFeature> features)
    {
        var settings = new XmlWriterSettings
        {
            Async = true,
            Encoding = System.Text.Encoding.UTF8,
            Indent = true,
            IndentChars = "  ",
        };
        await using var w = XmlWriter.Create(stream, settings);

        await w.WriteStartDocumentAsync();
        await w.WriteStartElementAsync(null, "kml", KmlNs);
        await w.WriteStartElementAsync(null, "Document", KmlNs);
        await w.WriteElementStringAsync(null, "name", KmlNs, layer.Name);

        // One shared style for the layer fallback color.
        string layerStyleId = "style_layer";
        await WriteStyleAsync(w, layerStyleId, layer.ColorHex, layer.Family);

        foreach (var feature in features)
        {
            var styleId = layerStyleId;

            // Feature-level stroke override (KML AABBGGRR).
            var stroke = ExtractStroke(feature.PropertiesJson);
            if (stroke is not null)
            {
                var featureStyleId = $"style_f{feature.Id}";
                await WriteStyleAsync(w, featureStyleId, stroke, layer.Family);
                styleId = featureStyleId;
            }

            await w.WriteStartElementAsync(null, "Placemark", KmlNs);
            if (!string.IsNullOrEmpty(feature.Name))
                await w.WriteElementStringAsync(null, "name", KmlNs, feature.Name);
            if (!string.IsNullOrEmpty(feature.Description))
                await w.WriteElementStringAsync(null, "description", KmlNs, feature.Description);

            await w.WriteElementStringAsync(null, "styleUrl", KmlNs, $"#{styleId}");
            await WriteGeometryAsync(w, feature);
            await w.WriteEndElementAsync(); // Placemark
        }

        await w.WriteEndElementAsync(); // Document
        await w.WriteEndElementAsync(); // kml
        await w.WriteEndDocumentAsync();
    }

    private static async Task WriteStyleAsync(XmlWriter w, string id, string colorHex,
        GeometryFamily family)
    {
        await w.WriteStartElementAsync(null, "Style", KmlNs);
        await w.WriteAttributeStringAsync(null, "id", null, id);

        if (family == GeometryFamily.Line || family == GeometryFamily.Polygon)
        {
            await w.WriteStartElementAsync(null, "LineStyle", KmlNs);
            await w.WriteElementStringAsync(null, "color", KmlNs, HexToKml(colorHex));
            await w.WriteElementStringAsync(null, "width", KmlNs, "3");
            await w.WriteEndElementAsync();
        }

        if (family == GeometryFamily.Polygon)
        {
            await w.WriteStartElementAsync(null, "PolyStyle", KmlNs);
            await w.WriteElementStringAsync(null, "color", KmlNs, "40" + RgbOnly(colorHex));
            await w.WriteEndElementAsync();
        }

        if (family == GeometryFamily.Point)
        {
            await w.WriteStartElementAsync(null, "IconStyle", KmlNs);
            await w.WriteElementStringAsync(null, "color", KmlNs, HexToKml(colorHex));
            await w.WriteEndElementAsync();
        }

        await w.WriteEndElementAsync(); // Style
    }

    private static async Task WriteGeometryAsync(XmlWriter w, MapFeature feature)
    {
        switch (feature.GeometryType)
        {
            case "Point":
                await w.WriteStartElementAsync(null, "Point", KmlNs);
                await w.WriteElementStringAsync(null, "coordinates", KmlNs,
                    CoordArrayToKml(feature.CoordinatesJson, isPoint: true));
                await w.WriteEndElementAsync();
                break;

            case "LineString":
                await w.WriteStartElementAsync(null, "LineString", KmlNs);
                await w.WriteElementStringAsync(null, "coordinates", KmlNs,
                    CoordArrayToKml(feature.CoordinatesJson, isPoint: false));
                await w.WriteEndElementAsync();
                break;

            case "Polygon":
                await w.WriteStartElementAsync(null, "Polygon", KmlNs);
                await w.WriteStartElementAsync(null, "outerBoundaryIs", KmlNs);
                await w.WriteStartElementAsync(null, "LinearRing", KmlNs);
                // CoordinatesJson for Polygon is [[ring], ...] â€” take first ring
                var ring = ExtractFirstRing(feature.CoordinatesJson);
                await w.WriteElementStringAsync(null, "coordinates", KmlNs,
                    CoordArrayToKml(ring, isPoint: false));
                await w.WriteEndElementAsync(); // LinearRing
                await w.WriteEndElementAsync(); // outerBoundaryIs
                await w.WriteEndElementAsync(); // Polygon
                break;
        }
    }

    // Converts a GeoJSON coord array [[lon,lat,?ele], ...] to KML "lon,lat,ele\n" text.
    private static string CoordArrayToKml(string coordsJson, bool isPoint)
    {
        try
        {
            using var doc = JsonDocument.Parse(coordsJson);
            var root = doc.RootElement;

            if (isPoint)
            {
                // [lon, lat] or [lon, lat, ele]
                var lon = root[0].GetDouble();
                var lat = root[1].GetDouble();
                var ele = root.GetArrayLength() > 2 ? root[2].GetDouble() : 0;
                return $"{lon.ToString("F6", CultureInfo.InvariantCulture)},{lat.ToString("F6", CultureInfo.InvariantCulture)},{ele.ToString("F1", CultureInfo.InvariantCulture)}";
            }

            var sb = new System.Text.StringBuilder();
            foreach (var pt in root.EnumerateArray())
            {
                var arr = pt.EnumerateArray().ToArray();
                var lon = arr[0].GetDouble();
                var lat = arr[1].GetDouble();
                var ele = arr.Length > 2 ? arr[2].GetDouble() : 0;
                sb.Append(lon.ToString("F6", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(lat.ToString("F6", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(ele.ToString("F1", CultureInfo.InvariantCulture));
                sb.Append('\n');
            }
            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }

    // For Polygon: CoordinatesJson is [[[lon,lat],...], ...] â€” extract outer ring.
    private static string ExtractFirstRing(string coordsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(coordsJson);
            var firstRing = doc.RootElement[0];
            return firstRing.GetRawText();
        }
        catch { return "[]"; }
    }

    private static string? ExtractStroke(string propsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(propsJson);
            return doc.RootElement.TryGetProperty("stroke", out var v) ? v.GetString() : null;
        }
        catch { return null; }
    }

    // #RRGGBB â†’ FFBBGGRR  (KML: alpha=FF, byte order reversed)
    private static string HexToKml(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
            return $"FF{hex[4..6]}{hex[2..4]}{hex[0..2]}".ToUpperInvariant();
        return "FFFFFFFF";
    }

    // #RRGGBB â†’ BBGGRR (without alpha prefix)
    private static string RgbOnly(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
            return $"{hex[4..6]}{hex[2..4]}{hex[0..2]}".ToUpperInvariant();
        return "FFFFFF";
    }
}
