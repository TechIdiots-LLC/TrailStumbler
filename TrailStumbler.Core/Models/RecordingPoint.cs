using SQLite;

namespace TrailStumbler.Core.Models;

/// <summary>One accepted GPS fix in a recording session.</summary>
[Table("RecordingPoints")]
public class RecordingPoint
{
    [PrimaryKey, AutoIncrement] public long Id { get; set; }
    [Indexed] public int SessionId { get; set; }
    public int? LayerId { get; set; }                // set after SaveRecordingAsLayerAsync; kept for GPX re-export
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double? Ele { get; set; }
    public long TimestampTicks { get; set; }   // UTC
    public double? AccuracyMeters { get; set; }
    public double? SpeedMps { get; set; }
    public double? Course { get; set; }
}
