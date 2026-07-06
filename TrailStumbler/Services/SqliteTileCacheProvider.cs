using MaplibreNative.Routing.Core.Mvt;
using SQLite;

namespace TrailStumbler.Services;

/// <summary>
/// Persists MVT tiles downloaded by the routing engine so subsequent route
/// calculations reuse cached bytes instead of re-fetching over HTTP.
/// The cache is shared with any future offline-map tile store (same DB path).
/// </summary>
public class SqliteTileCacheProvider : ITileCacheProvider
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
            await _db.CreateTableAsync<TileCacheRow>();
            _initialized = true;
            return _db;
        }
        finally { _initLock.Release(); }
    }

    public async Task<byte[]?> GetTileAsync(TileCoord coord, CancellationToken ct = default)
    {
        var db = await GetDbAsync();
        var key = Key(coord);
        var row = await db.FindAsync<TileCacheRow>(key);
        return row?.Data;
    }

    public async Task SetTileAsync(TileCoord coord, byte[] data, CancellationToken ct = default)
    {
        var db = await GetDbAsync();
        var row = new TileCacheRow { TileKey = Key(coord), Data = data };
        await db.InsertOrReplaceAsync(row);
    }

    public Task RequestAreaCacheAsync(
        double minLat, double minLon,
        double maxLat, double maxLon,
        int zoom,
        CancellationToken ct = default)
    {
        // No offline-area download manager integrated yet; tiles are cached
        // individually as the router fetches them during route calculation.
        return Task.CompletedTask;
    }

    private static string Key(TileCoord c) => $"{c.Z}/{c.X}/{c.Y}";
}

[Table("TileCache")]
file class TileCacheRow
{
    [PrimaryKey] public string TileKey { get; set; } = "";
    public byte[] Data { get; set; } = [];
}
