using System.Globalization;
using System.Text;
using System.Text.Json;
using MaplibreNative.Routing;
using MaplibreNative.Routing.Core.Models;
using SQLite;
using TrailStumbler.Core.GeoJson;
using TrailStumbler.Core.Models;
using TrailStumbler.Core.Services;

namespace TrailStumbler.Services;

/// <summary>
/// sqlite-net-pcl repository over a single persistent database in AppData.
/// Init pattern (SemaphoreSlim once-guard) follows VistumblerMAUI's SqliteDatabaseService.
/// </summary>
public class SqliteLayerRepository : ILayerRepository, IRouteDataSource
{
    private SQLiteAsyncConnection? _db;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static string DbPath => Path.Combine(FileSystem.AppDataDirectory, "TrailStumbler.db3");

    private async Task<SQLiteAsyncConnection> GetDbAsync()
    {
        if (_initialized) return _db!;
        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return _db!;
            _db = new SQLiteAsyncConnection(DbPath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
            await _db.CreateTableAsync<MapLayer>();
            await _db.CreateTableAsync<MapFeature>();
            await _db.CreateTableAsync<RecordingSession>();
            await _db.CreateTableAsync<RecordingPoint>();
            _initialized = true;
            return _db;
        }
        finally { _initLock.Release(); }
    }

    public async Task<List<MapLayer>> GetLayersAsync()
    {
        var db = await GetDbAsync();
        return await db.Table<MapLayer>().OrderBy(l => l.SortOrder).ToListAsync();
    }

    public async Task<MapLayer?> GetLayerAsync(int layerId)
    {
        var db = await GetDbAsync();
        return await db.FindAsync<MapLayer>(layerId);
    }

    public async Task<MapFeature?> GetFeatureAsync(long featureId)
    {
        var db = await GetDbAsync();
        return await db.FindAsync<MapFeature>(featureId);
    }

    public async Task<List<MapFeature>> GetFeaturesAsync(int layerId)
    {
        var db = await GetDbAsync();
        return await db.Table<MapFeature>().Where(f => f.LayerId == layerId).ToListAsync();
    }

    public async Task InsertLayerAsync(MapLayer layer, IReadOnlyList<MapFeature> features)
    {
        var db = await GetDbAsync();
        layer.SortOrder = await db.Table<MapLayer>().CountAsync();
        await db.RunInTransactionAsync(conn =>
        {
            conn.Insert(layer);   // sets layer.Id
            foreach (var chunk in features.Chunk(500))
            {
                foreach (var f in chunk) f.LayerId = layer.Id;
                conn.InsertAll(chunk, runInTransaction: false);
            }
        });
    }

    public async Task UpdateLayerAsync(MapLayer layer)
    {
        var db = await GetDbAsync();
        await db.UpdateAsync(layer);
    }

    public async Task DeleteLayerAsync(int layerId)
    {
        var db = await GetDbAsync();
        await db.RunInTransactionAsync(conn =>
        {
            conn.Execute("DELETE FROM Features WHERE LayerId = ?", layerId);
            conn.Execute("UPDATE RecordingPoints SET LayerId = NULL WHERE LayerId = ?", layerId);
            conn.Delete<MapLayer>(layerId);
        });
    }

    public async Task<string> BuildLayerGeoJsonAsync(int layerId, Func<MapFeature, bool>? include = null)
    {
        var features = await GetFeaturesAsync(layerId);
        return GeoJsonBuilder.BuildFeatureCollection(include is null ? features : features.Where(include).ToList());
    }

    // ── Recording session management ─────────────────────────────────────────

    public async Task<RecordingSession?> GetOpenRecordingSessionAsync()
    {
        var db = await GetDbAsync();
        return await db.Table<RecordingSession>().Where(s => !s.IsClosed).FirstOrDefaultAsync();
    }

    public async Task<int> StartRecordingSessionAsync(string name)
    {
        var db = await GetDbAsync();
        var session = new RecordingSession
        {
            Name = name,
            StartedAtTicks = DateTime.UtcNow.Ticks,
            IsClosed = false,
        };
        await db.InsertAsync(session);
        return session.Id;
    }

    public async Task AddRecordingPointAsync(RecordingPoint point)
    {
        var db = await GetDbAsync();
        await db.InsertAsync(point);
    }

    public async Task<List<RecordingPoint>> GetRecordingPointsAsync(int sessionId)
    {
        var db = await GetDbAsync();
        return await db.Table<RecordingPoint>()
            .Where(p => p.SessionId == sessionId)
            .OrderBy(p => p.TimestampTicks)
            .ToListAsync();
    }

    public async Task<MapLayer?> SaveRecordingAsLayerAsync(int sessionId)
    {
        var db = await GetDbAsync();
        var session = await db.FindAsync<RecordingSession>(sessionId);
        if (session is null) return null;

        var points = await GetRecordingPointsAsync(sessionId);
        if (points.Count < 2)
        {
            await DeleteRecordingSessionAsync(sessionId);
            return null;
        }

        var line = Core.Recording.RecordedTrack.Build(points)!;   // two or more points, none left out yet
        var propsJson = $"{{\"coordTimes\":{line.CoordTimesJson},\"stroke\":\"#FFFF66\"}}";

        var layer = new MapLayer
        {
            Name = session.Name,
            SourceFileName = "",
            GeometryFamily = (int)GeometryFamily.Line,
            ColorHex = "#FFFF66",
            IsVisible = true,
            IsRecordedTrack = true,
            DistanceMeters = line.DistanceMeters,
            FeatureCount = 1,
            MinLat = line.MinLat, MaxLat = line.MaxLat,
            MinLon = line.MinLon, MaxLon = line.MaxLon,
            ImportedAtTicks = DateTime.UtcNow.Ticks,
        };

        var feature = new MapFeature
        {
            GeometryType = line.GeometryType,
            CoordinatesJson = line.CoordinatesJson,
            PropertiesJson = propsJson,
            Name = session.Name,
            Description = "",
            MinLat = line.MinLat, MaxLat = line.MaxLat,
            MinLon = line.MinLon, MaxLon = line.MaxLon,
        };

        await InsertLayerAsync(layer, new[] { feature });

        // Link the raw points to the saved layer for GPX re-export.
        await db.ExecuteAsync("UPDATE RecordingPoints SET LayerId = ? WHERE SessionId = ?",
            layer.Id, sessionId);
        // Mark session closed (keep it so IsClosed query can distinguish from phantom sessions).
        session.IsClosed = true;
        await db.UpdateAsync(session);

        return layer;
    }

    public async Task DeleteRecordingSessionAsync(int sessionId)
    {
        var db = await GetDbAsync();
        await db.RunInTransactionAsync(conn =>
        {
            conn.Execute("DELETE FROM RecordingPoints WHERE SessionId = ?", sessionId);
            conn.Delete<RecordingSession>(sessionId);
        });
    }

    public async Task<bool> SetRecordingPointsExcludedAsync(MapLayer layer, IReadOnlyCollection<long> pointIds, bool excluded)
    {
        var db = await GetDbAsync();
        var points = await GetRecordingPointsByLayerAsync(layer.Id);
        var ids = pointIds.ToHashSet();
        var changed = points.Where(p => ids.Contains(p.Id) && p.Excluded != excluded).ToList();
        foreach (var p in changed) p.Excluded = excluded;
        var line = Core.Recording.RecordedTrack.Build(points);
        if (line is null) return false;   // nothing left to draw

        layer.DistanceMeters = line.DistanceMeters;
        layer.MinLat = line.MinLat; layer.MaxLat = line.MaxLat;
        layer.MinLon = line.MinLon; layer.MaxLon = line.MaxLon;
        await db.RunInTransactionAsync(conn =>
        {
            foreach (var p in changed) conn.Execute("UPDATE RecordingPoints SET Excluded = ? WHERE Id = ?", excluded, p.Id);
            var feature = conn.Table<MapFeature>().FirstOrDefault(f => f.LayerId == layer.Id);
            if (feature is not null)
            {
                feature.GeometryType = line.GeometryType;
                feature.CoordinatesJson = line.CoordinatesJson;
                feature.PropertiesJson = $"{{\"coordTimes\":{line.CoordTimesJson},\"stroke\":\"#FFFF66\"}}";
                feature.MinLat = line.MinLat; feature.MaxLat = line.MaxLat;
                feature.MinLon = line.MinLon; feature.MaxLon = line.MaxLon;
                conn.Update(feature);
            }
            conn.Update(layer);
        });
        return true;
    }

    public async Task<List<RecordingPoint>> GetRecordingPointsByLayerAsync(int layerId)
    {
        var db = await GetDbAsync();
        return await db.Table<RecordingPoint>()
            .Where(p => p.LayerId == layerId)
            .OrderBy(p => p.TimestampTicks)
            .ToListAsync();
    }

    // ── IRouteDataSource ─────────────────────────────────────────────────────

    public Task<IReadOnlyList<TrackFeature>> GetRoutableTrackFeaturesAsync() =>
        GetRoutableTrackFeaturesAsync(Core.Parsing.TrailVehicle.Any);

    /// <summary>
    /// The trails a route ridden on <paramref name="vehicle"/> may use: all of them, but those whose data says
    /// they're closed, closed to motorcycles, or narrower than the vehicle (<see cref="Core.Parsing.TrailAccess"/>).
    /// </summary>
    public async Task<IReadOnlyList<TrackFeature>> GetRoutableTrackFeaturesAsync(Core.Parsing.TrailVehicle vehicle)
    {
        var db = await GetDbAsync();
        // Include any visible line layer that has routing enabled.
        // Fall back to all visible line layers when none are explicitly opted in
        // (covers layers imported before IsUsedForRouting defaulted to true).
        var allLineLayers = await db.Table<Core.Models.MapLayer>()
            .Where(l => l.IsVisible && l.GeometryFamily == (int)Core.Models.GeometryFamily.Line && !l.IsRecordedTrack)
            .ToListAsync();
        var layers = allLineLayers.Any(l => l.IsUsedForRouting)
            ? allLineLayers.Where(l => l.IsUsedForRouting).ToList()
            : allLineLayers;

        var result = new List<TrackFeature>();
        foreach (var layer in layers)
        {
            var features = await db.Table<MapFeature>()
                .Where(f => f.LayerId == layer.Id && f.GeometryType == "LineString")
                .ToListAsync();

            foreach (var feature in features)
            {
                if (!Core.Parsing.TrailFlags.FromProperties(feature.PropertiesJson).Allows(vehicle)) continue;
                try
                {
                    var raw = JsonSerializer.Deserialize<double[][]>(feature.CoordinatesJson);
                    if (raw is null || raw.Length < 2) continue;
                    var coords = raw.Select(c => (Lon: c[0], Lat: c[1])).ToList();
                    result.Add(new TrackFeature
                    {
                        Coordinates = coords,
                        Name        = string.IsNullOrEmpty(feature.Name) ? layer.Name : feature.Name,
                        FeatureId   = feature.Id.ToString(),
                    });
                }
                catch { /* skip malformed coordinate JSON */ }
            }
        }
        return result;
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
