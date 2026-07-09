using System.Globalization;
using System.Text;
using TrailStumbler.Core.Models;

namespace TrailStumbler.Core.GeoJson;

/// <summary>
/// Streams a layer's features into a FeatureCollection JSON string. Coordinates and
/// properties are appended verbatim (they were stored as raw JSON text), with the
/// feature row id spliced in as "_fid" so a map tap can round-trip back to the row.
/// Pattern follows VistumblerCS's FireLiveApGeoJson StringBuilder streamer.
/// </summary>
public static class GeoJsonBuilder
{
    public const string EmptyFeatureCollection = """{"type":"FeatureCollection","features":[]}""";

    public static string BuildFeatureCollection(IReadOnlyList<MapFeature> features)
    {
        if (features.Count == 0) return EmptyFeatureCollection;

        int size = 64;
        foreach (var f in features)
            size += f.CoordinatesJson.Length + f.PropertiesJson.Length + 96;
        var sb = new StringBuilder(size);

        sb.Append("""{"type":"FeatureCollection","features":[""");
        for (int i = 0; i < features.Count; i++)
        {
            var f = features[i];
            if (i > 0) sb.Append(',');
            sb.Append("""{"type":"Feature","properties":{"_fid":""");
            sb.Append(f.Id.ToString(CultureInfo.InvariantCulture));
            AppendPropertiesTail(sb, f.PropertiesJson);
            sb.Append(""","geometry":{"type":""").Append('"').Append(f.GeometryType).Append('"')
              .Append(""","coordinates":""").Append(f.CoordinatesJson).Append("}}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    /// <summary>Append the stored properties object after "_fid":N — i.e. the object's
    /// members (if any) preceded by a comma, then the closing brace.</summary>
    private static void AppendPropertiesTail(StringBuilder sb, string propertiesJson)
    {
        var trimmed = propertiesJson.AsSpan().Trim();
        // "{}", "{ }", or malformed → just close the properties object.
        if (trimmed.Length < 3 || trimmed[0] != '{' || trimmed[1..^1].Trim().Length == 0)
        {
            sb.Append('}');
            return;
        }
        sb.Append(',');
        sb.Append(trimmed[1..]);   // members + closing brace, verbatim
    }
}
