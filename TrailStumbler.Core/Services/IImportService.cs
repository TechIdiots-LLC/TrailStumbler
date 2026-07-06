using TrailStumbler.Core.Models;

namespace TrailStumbler.Core.Services;

public record ImportProgress(string Stage, int Done, int Total);

public interface IImportService
{
    /// <summary>Import a GIS file into one or more layers (split per geometry family
    /// when mixed). Returns the created layers. Throws <see cref="FormatException"/>
    /// or <see cref="NotSupportedException"/> on unusable input.</summary>
    Task<List<MapLayer>> ImportFileAsync(
        string filePath,
        string displayName,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default);
}
