using TrailStumbler.Core.GeoJson;

namespace TrailStumbler.Services;

/// <summary>
/// Which categories of imported points the map shows (MAUI Preferences): what a rider needs by default, the town
/// services the trail maps also carry when asked for. A point of no listed category (a GPX waypoint) always shows.
/// </summary>
public static class PointCategorySettings
{
    public sealed record Entry(string Category, string Label, bool ShownByDefault);

    public static readonly Entry[] All =
    [
        new("fuel", "Fuel", true),
        new("parking", "Parking and trailheads", true),
        new("food", "Food", true),
        new("lodging", "Lodging", true),
        new("camping", "Camping", true),
        new("scenic_view", "Scenic places and parks", true),
        new("restroom", "Restrooms", true),
        new("atv_club", "ATV clubs", true),
        new("star", "Starred places", true),
        new("repair", "Garages and repair", false),
        new("fire_station", "Fire stations", false),
        new("police", "Police", false),
        new("bank", "Banks and ATMs", false),
        new("post_office", "Post offices", false),
        new("town_hall", "Town halls", false),
        new("pharmacy", "Pharmacies", false),
        new("laundry", "Laundromats", false),
        new("shop", "Shops", false),
        new(PointCategory.Other, "Other trail map points (homes, landmarks)", false),
    ];

    /// <summary>A category was shown or hidden.</summary>
    public static event Action? Changed;

    private static string Key(string category) => $"Points_Show_{category}";

    public static bool IsShown(string category) =>
        All.FirstOrDefault(e => e.Category == category) is not { } entry || Preferences.Get(Key(category), entry.ShownByDefault);

    public static void SetShown(string category, bool shown)
    {
        if (IsShown(category) == shown) return;
        Preferences.Set(Key(category), shown);
        Changed?.Invoke();
    }

    /// <summary>Whether the map shows a point with these properties.</summary>
    public static bool Shows(string propertiesJson) => PointCategory.Of(propertiesJson) is not { } c || IsShown(c);

    /// <summary>A category's name for people ("Banks and ATMs"), or the category as it is.</summary>
    public static string LabelOf(string category) => All.FirstOrDefault(e => e.Category == category)?.Label ?? category;
}
