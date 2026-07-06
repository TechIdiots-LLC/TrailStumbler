using SQLite;

namespace TrailStumbler.Core.Models;

/// <summary>One GeoJSON feature belonging to a layer. Coordinates are stored as the
/// raw GeoJSON coordinate-array text so the per-layer FeatureCollection can be
/// streamed back out without re-serialization.</summary>
[Table("Features")]
public class MapFeature
{
    [PrimaryKey, AutoIncrement] public long Id { get; set; }
    [Indexed] public int LayerId { get; set; }

    public string GeometryType { get; set; } = "";    // Point|LineString|MultiLineString|Polygon|â€¦
    public string CoordinatesJson { get; set; } = ""; // raw coordinates array text, verbatim
    public string PropertiesJson { get; set; } = "{}";// cleaned props (simplestyle, coordTimes, â€¦)

    // Denormalized for the tap popup without parsing PropertiesJson.
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";     // cleaned (DescriptionCleaner)

    // Per-feature bbox (zoom-to-feature / future spatial queries).
    public double MinLon { get; set; }
    public double MinLat { get; set; }
    public double MaxLon { get; set; }
    public double MaxLat { get; set; }
}
