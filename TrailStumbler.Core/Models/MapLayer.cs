using SQLite;

namespace TrailStumbler.Core.Models;

/// <summary>One toggleable map layer, produced by importing a file (possibly split
/// per geometry family) or by saving a recorded track.</summary>
[Table("Layers")]
public class MapLayer
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SourceFileName { get; set; } = "";
    public int GeometryFamily { get; set; }          // (int)Models.GeometryFamily
    public string ColorHex { get; set; } = "#E53935";
    public bool IsVisible { get; set; } = true;      // checked by default on import
    public int FeatureCount { get; set; }

    // Layer bounding box, for zoom-to-layer fit-bounds.
    public double MinLon { get; set; }
    public double MinLat { get; set; }
    public double MaxLon { get; set; }
    public double MaxLat { get; set; }

    public long ImportedAtTicks { get; set; }
    public bool IsRecordedTrack { get; set; }
    public double? DistanceMeters { get; set; }      // set for recorded tracks
    public int SortOrder { get; set; }               // list order & map z-order
    public bool IsUsedForRouting { get; set; }       // include in offline A* / hybrid routing

    [Ignore] public GeometryFamily Family => (GeometryFamily)GeometryFamily;
}
