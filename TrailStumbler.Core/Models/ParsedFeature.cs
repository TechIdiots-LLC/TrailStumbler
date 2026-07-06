using TrailStumbler.Core.GeoJson;

namespace TrailStumbler.Core.Models;

/// <summary>Parser output for one feature, before it is bucketed into layers and
/// written to the database.</summary>
public class ParsedFeature
{
    public string GeometryType { get; set; } = "";
    public string CoordinatesJson { get; set; } = "";  // raw GeoJSON coordinates array text
    public string PropertiesJson { get; set; } = "{}";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";      // cleaned
    public BoundingBox Bbox { get; } = new();

    public GeometryFamily Family => GeometryFamilyExtensions.FamilyOf(GeometryType);
}
