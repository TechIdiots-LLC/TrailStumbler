using System.Text.Json;
using TrailStumbler.Core.Models;

namespace TrailStumbler.Core.Parsing;

/// <summary>
/// Parses GeoJSON (FeatureCollection, single Feature, or bare geometry) into
/// <see cref="ParsedFeature"/>s. Coordinate arrays are captured verbatim with
/// GetRawText() so they round-trip byte-for-byte to the map.
/// </summary>
public class GeoJsonParser : IGisParser
{
    public List<ParsedFeature> Parse(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeEl))
            throw new FormatException("Not a GeoJSON document (no \"type\" member).");

        var results = new List<ParsedFeature>();
        switch (typeEl.GetString())
        {
            case "FeatureCollection":
                if (root.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array)
                    foreach (var f in features.EnumerateArray())
                        AddFeature(f, results);
                break;
            case "Feature":
                AddFeature(root, results);
                break;
            default: // bare geometry
                AddGeometry(root, default, results);
                break;
        }
        return results;
    }

    private static void AddFeature(JsonElement feature, List<ParsedFeature> results)
    {
        if (!feature.TryGetProperty("geometry", out var geometry) ||
            geometry.ValueKind != JsonValueKind.Object)
            return;
        feature.TryGetProperty("properties", out var props);
        AddGeometry(geometry, props, results);
    }

    private static void AddGeometry(JsonElement geometry, JsonElement props, List<ParsedFeature> results)
    {
        if (!geometry.TryGetProperty("type", out var typeEl)) return;
        var type = typeEl.GetString() ?? "";

        // Flatten GeometryCollection into one feature per member (same deviation as
        // the KML parser) so geometry-family splitting stays uniform.
        if (type == "GeometryCollection")
        {
            if (geometry.TryGetProperty("geometries", out var members) && members.ValueKind == JsonValueKind.Array)
                foreach (var g in members.EnumerateArray())
                    AddGeometry(g, props, results);
            return;
        }

        if (!geometry.TryGetProperty("coordinates", out var coords)) return;

        var pf = new ParsedFeature
        {
            GeometryType = type,
            CoordinatesJson = coords.GetRawText(),
            PropertiesJson = props.ValueKind == JsonValueKind.Object ? props.GetRawText() : "{}",
        };
        if (props.ValueKind == JsonValueKind.Object)
        {
            pf.Name = GetString(props, "name", "Name", "title") ?? "";
            pf.Description = DescriptionCleaner.CleanForDisplay(GetString(props, "description", "desc") ?? "");
        }
        WalkCoordinates(coords, pf);
        if (!pf.Bbox.IsEmpty) results.Add(pf);
    }

    /// <summary>Recursively accumulate every [lon, lat, …] position into the bbox.</summary>
    internal static void WalkCoordinates(JsonElement coords, ParsedFeature pf)
    {
        if (coords.ValueKind != JsonValueKind.Array || coords.GetArrayLength() == 0) return;
        if (coords[0].ValueKind == JsonValueKind.Number)
        {
            if (coords.GetArrayLength() >= 2)
                pf.Bbox.Extend(coords[0].GetDouble(), coords[1].GetDouble());
            return;
        }
        foreach (var child in coords.EnumerateArray())
            WalkCoordinates(child, pf);
    }

    private static string? GetString(JsonElement props, params string[] names)
    {
        foreach (var n in names)
            if (props.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        return null;
    }
}
