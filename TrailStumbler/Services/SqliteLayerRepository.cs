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

    public async Task<string> BuildLayerGeoJsonAsync(int layerId)
    {
        var features = await GetFeaturesAsync(layerId);
        return GeoJsonBuilder.BuildFeatureCollection(features);
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

        // Build GeoJSON coordinate array [[lon, lat, ele?], ...]
        var coordsBuilder = new StringBuilder("[");
        var coordTimesBuilder = new StringBuilder("[");
        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLon = double.MaxValue, maxLon = double.MinValue;
        double totalDistM = 0;
        RecordingPoint? prev = null;

        for (int i = 0; i < points.Count; i++)
        {
            var pt = points[i];
            if (i > 0) { coordsBuilder.Append(','); coordTimesBuilder.Append(','); }

            coordsBuilder.Append('[');
            coordsBuilder.Append(pt.Lon.ToString("F6", CultureInfo.InvariantCulture));
            coordsBuilder.Append(',');
            coordsBuilder.Append(pt.Lat.ToString("F6", CultureInfo.InvariantCulture));
            if (pt.Ele.HasValue)
            {
                coordsBuilder.Append(',');
                coordsBuilder.Append(pt.Ele.Value.ToString("F1", CultureInfo.InvariantCulture));
            }
            coordsBuilder.Append(']');

            var ts = new DateTime(pt.TimestampTicks, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ");
            coordTimesBuilder.Append('"'); coordTimesBuilder.Append(ts); coordTimesBuilder.Append('"');

            if (pt.Lat < minLat) minLat = pt.Lat;
            if (pt.Lat > maxLat) maxLat = pt.Lat;
            if (pt.Lon < minLon) minLon = pt.Lon;
            if (pt.Lon > maxLon) maxLon = pt.Lon;

            if (prev is not null)
                totalDistM += Haversine(prev.Lat, prev.Lon, pt.Lat, pt.Lon);
            prev = pt;
        }
        coordsBuilder.Append(']');
        coordTimesBuilder.Append(']');

        var propsJson = $"{{\"coordTimes\":{coordTimesBuilder},\"stroke\":\"#FFFF66\"}}";

        var layer = new MapLayer
        {
            Name = session.Name,
            SourceFileName = "",
            GeometryFamily = (int)GeometryFamily.Line,
            ColorHex = "#FFFF66",
            IsVisible = true,
            IsRecordedTrack = true,
            DistanceMeters = totalDistM,
            FeatureCount = 1,
            MinLat = minLat, MaxLat = maxLat,
            MinLon = minLon, MaxLon = maxLon,
            ImportedAtTicks = DateTime.UtcNow.Ticks,
        };

        var feature = new MapFeature
        {
            GeometryType = "LineString",
            CoordinatesJson = coordsBuilder.ToString(),
            PropertiesJson = propsJson,
            Name = session.Name,
            Description = "",
            MinLat = minLat, MaxLat = maxLat,
            MinLon = minLon, MaxLon = maxLon,
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

    public async Task<List<RecordingPoint>> GetRecordingPointsByLayerAsync(int layerId)
    {
        var db = await GetDbAsync();
        return await db.Table<RecordingPoint>()
            .Where(p => p.LayerId == layerId)
            .OrderBy(p => p.TimestampTicks)
            .ToListAsync();
    }

    // ── IRouteDataSource ─────────────────────────────────────────────────────

    public async Task<IReadOnlyList<TrackFeature>> GetRoutableTrackFeaturesAsync()
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
