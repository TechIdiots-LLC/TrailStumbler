using System.Text;
using CommunityToolkit.Maui.Storage;
using TrailStumbler.Core.Export;
using TrailStumbler.Core.GeoJson;
using TrailStumbler.Core.Models;
using TrailStumbler.Core.Services;

namespace TrailStumbler.Services;

public class ExportService
{
    private readonly ILayerRepository _repo;

    public ExportService(ILayerRepository repo) => _repo = repo;

    /// <summary>Exports a recorded track layer to GPX. Returns true on success.</summary>
    public async Task<bool> ExportGpxAsync(MapLayer layer, CancellationToken ct = default)
    {
        var points = await _repo.GetRecordingPointsByLayerAsync(layer.Id);
        if (points.Count == 0) return false;

        using var ms = new MemoryStream();
        await GpxWriter.WriteAsync(ms, layer.Name, points);
        ms.Position = 0;

        var result = await FileSaver.Default.SaveAsync(SanitizeFileName(layer.Name) + ".gpx", ms, ct);
        return result.IsSuccessful;
    }

    /// <summary>Exports any layer to a GeoJSON FeatureCollection file.</summary>
    public async Task<bool> ExportGeoJsonAsync(MapLayer layer, CancellationToken ct = default)
    {
        var geoJson = await _repo.BuildLayerGeoJsonAsync(layer.Id);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(geoJson));
        var result = await FileSaver.Default.SaveAsync(SanitizeFileName(layer.Name) + ".geojson", ms, ct);
        return result.IsSuccessful;
    }

    /// <summary>Exports any layer to a KML 2.2 file.</summary>
    public async Task<bool> ExportKmlAsync(MapLayer layer, CancellationToken ct = default)
    {
        var features = await _repo.GetFeaturesAsync(layer.Id);
        using var ms = new MemoryStream();
        await KmlWriter.WriteAsync(ms, layer, features);
        ms.Position = 0;

        var result = await FileSaver.Default.SaveAsync(SanitizeFileName(layer.Name) + ".kml", ms, ct);
        return result.IsSuccessful;
    }

    /// <summary>Several layers (imported and recorded alike) to one GeoJSON, KML or GPX file.</summary>
    public async Task<bool> ExportLayersAsync(IReadOnlyList<MapLayer> layers, string format, string fileName,
        CancellationToken ct = default)
    {
        var withFeatures = new List<(MapLayer Layer, IReadOnlyList<MapFeature> Features)>();
        foreach (var layer in layers)
            withFeatures.Add((layer, await _repo.GetFeaturesAsync(layer.Id)));

        using var ms = new MemoryStream();
        string ext;
        switch (format)
        {
            case "GeoJSON":
                var json = GeoJsonBuilder.BuildFeatureCollection(withFeatures.Select(l => (l.Layer.Name, l.Features)).ToList());
                await ms.WriteAsync(Encoding.UTF8.GetBytes(json), ct);
                ext = ".geojson";
                break;
            case "KML":
                await KmlWriter.WriteLayersAsync(ms, fileName, withFeatures);
                ext = ".kml";
                break;
            case "GPX":
                var gpx = new List<GpxLayer>();
                foreach (var (layer, features) in withFeatures)
                    gpx.Add(new GpxLayer(layer, features,
                        layer.IsRecordedTrack ? await _repo.GetRecordingPointsByLayerAsync(layer.Id) : null));
                await GpxWriter.WriteLayersAsync(ms, gpx);
                ext = ".gpx";
                break;
            default:
                return false;
        }
        ms.Position = 0;
        var result = await FileSaver.Default.SaveAsync(SanitizeFileName(fileName) + ext, ms, ct);
        return result.IsSuccessful;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
