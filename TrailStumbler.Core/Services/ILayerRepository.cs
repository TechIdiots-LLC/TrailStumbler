using TrailStumbler.Core.Models;

namespace TrailStumbler.Core.Services;

/// <summary>Persistent store for layers and their features (SQLite in the app).</summary>
public interface ILayerRepository
{
    Task<List<MapLayer>> GetLayersAsync();
    Task<MapLayer?> GetLayerAsync(int layerId);
    Task<MapFeature?> GetFeatureAsync(long featureId);
    Task<List<MapFeature>> GetFeaturesAsync(int layerId);

    /// <summary>Insert a layer and its features (batched, in a transaction).
    /// Sets the layer id and SortOrder.</summary>
    Task InsertLayerAsync(MapLayer layer, IReadOnlyList<MapFeature> features);

    Task UpdateLayerAsync(MapLayer layer);
    Task DeleteLayerAsync(int layerId);

    /// <summary>Build the layer's FeatureCollection GeoJSON string.</summary>
    Task<string> BuildLayerGeoJsonAsync(int layerId);

    // ── Recording session management ─────────────────────────────────────────

    /// <summary>Returns an unclosed session if the app crashed mid-recording.</summary>
    Task<RecordingSession?> GetOpenRecordingSessionAsync();

    Task<int> StartRecordingSessionAsync(string name);
    Task AddRecordingPointAsync(RecordingPoint point);
    Task<List<RecordingPoint>> GetRecordingPointsAsync(int sessionId);

    /// <summary>Converts accepted points into a MapLayer + LineString feature, sets
    /// RecordingPoint.LayerId on each point (for GPX re-export), marks the session
    /// closed. Returns null if fewer than 2 points.</summary>
    Task<MapLayer?> SaveRecordingAsLayerAsync(int sessionId);

    Task DeleteRecordingSessionAsync(int sessionId);

    /// <summary>Returns the accepted GPS points linked to a saved recorded-track layer.</summary>
    Task<List<RecordingPoint>> GetRecordingPointsByLayerAsync(int layerId);

    /// <summary>
    /// Leaves points of a recorded track out (or puts them back) and redraws the track from the rest: its line, its
    /// distance and bounds, on <paramref name="layer"/> too. False, and nothing changed, when it would keep no two
    /// points in a row.
    /// </summary>
    Task<bool> SetRecordingPointsExcludedAsync(MapLayer layer, IReadOnlyCollection<long> pointIds, bool excluded);
}

