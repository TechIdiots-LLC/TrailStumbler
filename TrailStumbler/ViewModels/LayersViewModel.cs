using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrailStumbler.Core.Models;
using TrailStumbler.Core.Parsing;
using TrailStumbler.Core.Services;
using TrailStumbler.Services;

namespace TrailStumbler.ViewModels;

public partial class LayersViewModel : ObservableObject
{
    private readonly ILayerRepository _repo;
    private readonly IImportService _import;
    private readonly MapViewModel _map;
    private readonly ExportService _export;

    public ObservableCollection<LayerItemViewModel> ImportedLayers { get; } = new();
    public ObservableCollection<RecordingItemViewModel> RecordedTracks { get; } = new();

    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private string _importStatus = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecordedTracks))]
    private int _recordedTrackCount;

    public bool HasRecordedTracks => _recordedTrackCount > 0;

    private bool _loaded;

    public LayersViewModel(ILayerRepository repo, IImportService import, MapViewModel map,
        ExportService export)
    {
        _repo = repo;
        _import = import;
        _map = map;
        _export = export;

        _map.RecordingSaved += OnRecordingSaved;
        _map.TrackEdited += (_, layer) => RecordedTracks.FirstOrDefault(t => ReferenceEquals(t.Layer, layer))?.Refresh();
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var layers = await _repo.GetLayersAsync();
        ImportedLayers.Clear();
        RecordedTracks.Clear();
        foreach (var layer in layers)
        {
            if (layer.IsRecordedTrack)
                RecordedTracks.Add(new RecordingItemViewModel(layer, this));
            else
                ImportedLayers.Add(new LayerItemViewModel(layer, this));
        }
        RecordedTrackCount = RecordedTracks.Count;
    }

    private void OnRecordingSaved(object? sender, MapLayer layer)
    {
        RecordedTracks.Add(new RecordingItemViewModel(layer, this));
        RecordedTrackCount = RecordedTracks.Count;
    }

    // ── Import ────────────────────────────────────────────────────────────────

    private static readonly FilePickerFileType GisFileTypes = new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.WinUI] = new[] { ".geojson", ".json", ".kml", ".kmz", ".gpx", ".img" },
            [DevicePlatform.Android] = new[] { "*/*" },
        });

    [RelayCommand]
    private async Task ImportAsync()
    {
        try
        {
            var picked = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "Import GIS files",
                FileTypes = GisFileTypes,
            });
            if (picked is null || !picked.Any()) return;

            IsImporting = true;
            var files = picked.ToList();
            var totalImported = new List<MapLayer>();

            foreach (var file in files)
            {
                ImportStatus = $"Importing {file.FileName}…";

                var tempPath = Path.Combine(FileSystem.CacheDirectory, file.FileName);
                await using (var src = await file.OpenReadAsync())
                await using (var dst = File.Create(tempPath))
                    await src.CopyToAsync(dst);

                var progress = new Progress<ImportProgress>(p =>
                    ImportStatus = p.Total > 0 ? $"{file.FileName}: {p.Stage} {p.Done}/{p.Total}" : $"{file.FileName}: {p.Stage}");

                var options = await AskForBackgroundAsync(tempPath, file.FileName);
                var created = await Task.Run(() => _import.ImportFileAsync(tempPath, file.FileName, progress, options: options));
                File.Delete(tempPath);

                foreach (var layer in created)
                    ImportedLayers.Add(new LayerItemViewModel(layer, this));
                totalImported.AddRange(created);
                await _map.OnLayersImportedAsync(created);
            }

            ImportStatus = totalImported.Count == 1
                ? $"Imported {totalImported[0].Name} ({totalImported[0].FeatureCount} features)"
                : $"Imported {totalImported.Count} layers from {files.Count} file(s)";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LayersViewModel] Import failed: {ex}");
            ImportStatus = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsImporting = false;
        }
    }

    /// <summary>A Garmin map with its maker's background map in it: import that too? Left out unless asked for.</summary>
    private static async Task<ImportOptions?> AskForBackgroundAsync(string path, string fileName)
    {
        int maps;
        try
        {
            if (FormatDetector.Detect(path) != GisFormat.GarminImg) return null;
            await using var stream = File.OpenRead(path);
            maps = ImgParser.BackgroundMapCount(stream);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LayersViewModel] Background check failed: {ex.Message}");
            return null;
        }
        if (maps == 0) return null;
        bool include = await Shell.Current.DisplayAlertAsync("Background map",
            $"{fileName} includes a background map ({maps} part{(maps == 1 ? "" : "s")}: roads, water, towns) " +
            "beside its trails. The trails are imported either way.\n\n" +
            "The background can be millions of features: minutes to import, and slow to show. It gets layers of its " +
            "own, hidden until you turn them on, and isn't used for routing.",
            "Include background", "Trails only");
        return new ImportOptions(IncludeBackground: include);
    }

    // ── Row callbacks — imported layers ───────────────────────────────────────

    public async Task OnItemVisibilityChangedAsync(LayerItemViewModel item, bool visible)
    {
        try
        {
            item.Layer.IsVisible = visible;
            await _repo.UpdateLayerAsync(item.Layer);
            await _map.SetLayerVisibilityAsync(item.Layer, visible);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LayersViewModel] Visibility change failed: {ex}");
        }
    }

    public async Task OnItemRoutingChangedAsync(LayerItemViewModel item, bool usedForRouting)
    {
        try
        {
            item.Layer.IsUsedForRouting = usedForRouting;
            await _repo.UpdateLayerAsync(item.Layer);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LayersViewModel] Routing toggle failed: {ex}");
        }
    }

    public async Task ZoomToLayerAsync(LayerItemViewModel item)
    {
        if (!item.IsVisible) item.IsVisible = true;
        _map.RequestFitBounds(item.Layer);
        await Shell.Current.GoToAsync("//MapPage");
    }

    private static readonly (string Name, string Hex)[] ColorChoices =
    [
        ("Red", "#E53935"), ("Blue", "#1E88E5"), ("Green", "#43A047"), ("Orange", "#FB8C00"),
        ("Purple", "#8E24AA"), ("Cyan", "#00ACC1"), ("Deep Orange", "#F4511E"), ("Indigo", "#3949AB"),
        ("Yellow", "#FDD835"), ("Black", "#212121"),
    ];

    public async Task ChangeLayerColorAsync(LayerItemViewModel item)
    {
        var choice = await Shell.Current.DisplayActionSheetAsync(
            $"Color for {item.Name}", "Cancel", null, ColorChoices.Select(c => c.Name).ToArray());
        var hex = ColorChoices.FirstOrDefault(c => c.Name == choice).Hex;
        if (hex is null || hex == item.Layer.ColorHex) return;

        item.Layer.ColorHex = hex;
        await _repo.UpdateLayerAsync(item.Layer);
        item.NotifyColorChanged();
        await _map.ApplyLayerColorAsync(item.Layer);
    }

    public async Task DeleteLayerAsync(LayerItemViewModel item)
    {
        bool confirm = await Shell.Current.DisplayAlertAsync(
            "Delete layer", $"Delete '{item.Name}' and its {item.Layer.FeatureCount} features?",
            "Delete", "Cancel");
        if (!confirm) return;

        await _repo.DeleteLayerAsync(item.Layer.Id);
        _map.OnLayerDeleted(item.Layer);
        ImportedLayers.Remove(item);
    }

    public async Task ExportLayerAsync(LayerItemViewModel item)
    {
        var format = await Shell.Current.DisplayActionSheetAsync(
            $"Export '{item.Name}'", "Cancel", null, "GeoJSON", "KML");
        switch (format)
        {
            case "GeoJSON":
                await _export.ExportGeoJsonAsync(item.Layer);
                break;
            case "KML":
                await _export.ExportKmlAsync(item.Layer);
                break;
        }
    }

    /// <summary>Export several layers to one file: all of them, or those shown on the map (the checkboxes choose).</summary>
    [RelayCommand]
    private async Task ExportLayersAsync()
    {
        var all = ImportedLayers.Select(l => l.Layer).Concat(RecordedTracks.Select(t => t.Layer)).ToList();
        if (all.Count == 0)
        {
            await Shell.Current.DisplayAlertAsync("Export", "There are no layers to export yet.", "OK");
            return;
        }
        var shown = all.Where(l => l.IsVisible).ToList();
        string allChoice = $"All layers ({all.Count})", shownChoice = $"Shown layers ({shown.Count})";
        var which = await Shell.Current.DisplayActionSheetAsync("Export layers", "Cancel", null,
            shown.Count > 0 && shown.Count < all.Count ? [allChoice, shownChoice] : [allChoice]);
        var layers = which == allChoice ? all : which == shownChoice ? shown : null;
        if (layers is null) return;

        var format = await Shell.Current.DisplayActionSheetAsync($"Export {layers.Count} layer(s) as", "Cancel", null,
            "GeoJSON", "KML", "GPX");
        if (format is not ("GeoJSON" or "KML" or "GPX")) return;
        try
        {
            string name = layers.Count == 1 ? layers[0].Name : $"TrailStumbler {DateTime.Now:yyyy-MM-dd}";
            ImportStatus = $"Exporting {layers.Count} layer(s)…";
            bool saved = await _export.ExportLayersAsync(layers, format, name);
            ImportStatus = saved ? $"Exported {layers.Count} layer(s) as {format}" : "";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LayersViewModel] Export failed: {ex}");
            ImportStatus = "";
            await Shell.Current.DisplayAlertAsync("Export failed", ex.Message, "OK");
        }
    }

    // ── Row callbacks — recorded tracks ───────────────────────────────────────

    public async Task OnItemVisibilityChangedAsync(RecordingItemViewModel item, bool visible)
    {
        try
        {
            item.Layer.IsVisible = visible;
            await _repo.UpdateLayerAsync(item.Layer);
            await _map.SetLayerVisibilityAsync(item.Layer, visible);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LayersViewModel] Visibility change failed: {ex}");
        }
    }

    public async Task ZoomToLayerAsync(RecordingItemViewModel item)
    {
        if (!item.IsVisible) item.IsVisible = true;
        _map.RequestFitBounds(item.Layer);
        await Shell.Current.GoToAsync("//MapPage");
    }

    /// <summary>Edit a recording on the map: leave points out of it, or put them back.</summary>
    public async Task EditTrackAsync(RecordingItemViewModel item)
    {
        await _map.BeginTrackEditAsync(item.Layer);
        await Shell.Current.GoToAsync("//MapPage");
    }

    public async Task ExportTrackGpxAsync(RecordingItemViewModel item)
    {
        try
        {
            await _export.ExportGpxAsync(item.Layer);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LayersViewModel] ExportGpx failed: {ex}");
            await Shell.Current.DisplayAlertAsync("Export failed", ex.Message, "OK");
        }
    }

    public async Task DeleteRecordedTrackAsync(RecordingItemViewModel item)
    {
        bool confirm = await Shell.Current.DisplayAlertAsync(
            "Delete recording", $"Delete '{item.Name}'?", "Delete", "Cancel");
        if (!confirm) return;

        await _repo.DeleteLayerAsync(item.Layer.Id);
        _map.OnLayerDeleted(item.Layer);
        RecordedTracks.Remove(item);
        RecordedTrackCount = RecordedTracks.Count;
    }
}
