using TrailStumbler.Core.Models;

namespace TrailStumbler.Core.Services;

public record ImportProgress(string Stage, int Done, int Total);

/// <summary>
/// <paramref name="IncludeBackground"/>: a Garmin map's background map (roads, water, towns) imported too, as
/// layers of its own, hidden and not routed over; left out by default.
/// </summary>
public record ImportOptions(bool IncludeBackground = false);

public interface IImportService
{
    /// <summary>Import a GIS file into one or more layers (split per geometry family
    /// when mixed). Returns the created layers. Throws <see cref="FormatException"/>
    /// or <see cref="NotSupportedException"/> on unusable input.</summary>
    Task<List<MapLayer>> ImportFileAsync(
        string filePath,
        string displayName,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default,
        ImportOptions? options = null);
}
