namespace TrailStumbler.Services;

/// <summary>
/// Basemap style presets and the persisted selection (MAUI Preferences).
/// Same presets as VistumblerMAUI/atv-trail-map — tiles.wifidb.net styles.
/// </summary>
public static class MapStyles
{
    private const string StyleUrlKey = "Map_StyleUrl";

    public const string DefaultUrl = "https://tiles.wifidb.net/styles/WDB_OSM/style.json";

    public const string CustomName = "Custom…";

    public static readonly IReadOnlyList<(string Name, string Url)> Presets = new[]
    {
        ("WifiDB OSM",                 "https://tiles.wifidb.net/styles/WDB_OSM/style.json"),
        ("WifiDB Color Relief",        "https://tiles.wifidb.net/styles/WDB_COLOR_RELIEF/style.json"),
        ("WifiDB Color Relief (Dark)", "https://tiles.wifidb.net/styles/WDB_COLOR_RELIEF_DARK/style.json"),
    };

    public static string StyleUrl
    {
        get => Preferences.Get(StyleUrlKey, DefaultUrl);
        set => Preferences.Set(StyleUrlKey, string.IsNullOrWhiteSpace(value) ? DefaultUrl : value.Trim());
    }
}
