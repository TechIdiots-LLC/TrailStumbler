using TrailStumbler.Core.Models;
using TrailStumbler.Core.Parsing;
using TrailStumbler.Core.Services;

namespace TrailStumbler.Services;

/// <summary>
/// Imports a GIS file: detect format → parse → split by geometry family → insert.
/// Splitting: a file containing ≥2 geometry families becomes one layer per family
/// ("(trails)" / "(points)" / "(areas)") so e.g. GPS Trail Masters KMLs get
/// separately toggleable trail and waypoint layers.
/// </summary>
public class ImportService : IImportService
{
    private readonly ILayerRepository _repo;

    public ImportService(ILayerRepository repo) => _repo = repo;

    public async Task<List<MapLayer>> ImportFileAsync(
        string filePath, string displayName,
        IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
    {
        progress?.Report(new ImportProgress("Reading file…", 0, 0));

        var format = FormatDetector.Detect(filePath);
        IGisParser parser = format switch
        {
            GisFormat.GeoJson => new GeoJsonParser(),
            GisFormat.Gpx => new GpxParser(),
            GisFormat.Kml => new KmlParser(),
            GisFormat.Kmz => new KmzParser(),
            GisFormat.GarminImg => new ImgParser(),
            _ => throw new FormatException($"'{displayName}' is not a recognized GIS file (GeoJSON/GPX/KML/KMZ/Garmin IMG)."),
        };

        List<ParsedFeature> parsed;
        await using (var stream = File.OpenRead(filePath))
            parsed = await Task.Run(() => parser.Parse(stream), ct);

        if (parsed.Count == 0)
            throw new FormatException($"No mappable features found in '{displayName}'.");

        progress?.Report(new ImportProgress("Saving…", 0, parsed.Count));

        var baseName = Path.GetFileNameWithoutExtension(displayName);
        var buckets = parsed
            .GroupBy(f => f.Family)
            .OrderBy(g => g.Key)   // points first, then lines, then areas
            .ToList();

        var existingCount = (await _repo.GetLayersAsync()).Count;
        var created = new List<MapLayer>();
        int saved = 0;

        foreach (var bucket in buckets)
        {
            ct.ThrowIfCancellationRequested();

            var suffix = buckets.Count == 1 ? "" : bucket.Key switch
            {
                GeometryFamily.Point => " (points)",
                GeometryFamily.Line => " (trails)",
                _ => " (areas)",
            };

            var layer = new MapLayer
            {
                Name = baseName + suffix,
                SourceFileName = displayName,
                GeometryFamily = (int)bucket.Key,
                ColorHex = LayerPalette.ColorFor(existingCount + created.Count),
                IsVisible = true,
                IsUsedForRouting = bucket.Key == GeometryFamily.Line,
                ImportedAtTicks = DateTime.UtcNow.Ticks,
            };

            var features = new List<MapFeature>();
            var bbox = new Core.GeoJson.BoundingBox();
            foreach (var pf in bucket)
            {
                features.Add(new MapFeature
                {
                    GeometryType = pf.GeometryType,
                    CoordinatesJson = pf.CoordinatesJson,
                    PropertiesJson = pf.PropertiesJson,
                    Name = pf.Name,
                    Description = pf.Description,
                    MinLon = pf.Bbox.MinLon, MinLat = pf.Bbox.MinLat,
                    MaxLon = pf.Bbox.MaxLon, MaxLat = pf.Bbox.MaxLat,
                });
                bbox.Union(pf.Bbox);
            }

            layer.FeatureCount = features.Count;
            layer.MinLon = bbox.MinLon; layer.MinLat = bbox.MinLat;
            layer.MaxLon = bbox.MaxLon; layer.MaxLat = bbox.MaxLat;

            await _repo.InsertLayerAsync(layer, features);
            created.Add(layer);

            saved += features.Count;
            progress?.Report(new ImportProgress("Saving…", saved, parsed.Count));
        }

        return created;
    }
}
