using System.Text.Json;
using TrailStumbler.Core.Parsing.Garmin;

namespace TrailStumbler.Core.GeoJson;

/// <summary>The category a point is shown or hidden by, for the map's per-category settings.</summary>
public static class PointCategory
{
    /// <summary>A trail map's catch-all point whose name matched no category (Garmin's generic type).</summary>
    public const string Other = "other";

    /// <summary>
    /// The point's <c>category</c> property; <see cref="Other"/> for a catch-all Garmin point without one; null for
    /// any other point (a GPX waypoint, a KML placemark without an icon), which no setting hides.
    /// </summary>
    public static string? Of(string propertiesJson)
    {
        if (string.IsNullOrWhiteSpace(propertiesJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(propertiesJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String) return c.GetString();
            return root.TryGetProperty("garmin_type", out var t) && t.ValueKind == JsonValueKind.String &&
                   t.GetString() == GarminTypeClasses.GenericPointType ? Other : null;
        }
        catch (JsonException) { return null; }
    }
}
