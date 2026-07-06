namespace TrailStumbler.Core.Models;

/// <summary>Broad geometry bucket a layer holds. Mixed imports are split into one
/// layer per family so trails and waypoints toggle/style independently.</summary>
public enum GeometryFamily
{
    Point = 0,
    Line = 1,
    Polygon = 2,
}

public static class GeometryFamilyExtensions
{
    /// <summary>Map a GeoJSON geometry type name to its family.</summary>
    public static GeometryFamily FamilyOf(string geometryType) => geometryType switch
    {
        "Point" or "MultiPoint" => GeometryFamily.Point,
        "LineString" or "MultiLineString" => GeometryFamily.Line,
        "Polygon" or "MultiPolygon" => GeometryFamily.Polygon,
        _ => throw new NotSupportedException($"Unsupported geometry type '{geometryType}'"),
    };
}
