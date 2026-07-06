using SQLite;

namespace TrailStumbler.Core.Models;

/// <summary>A live track-recording session. Left unclosed on crash so the app can
/// offer to recover the points into a track at next startup.</summary>
[Table("RecordingSessions")]
public class RecordingSession
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string Name { get; set; } = "";
    public long StartedAtTicks { get; set; }
    public bool IsClosed { get; set; }
}
