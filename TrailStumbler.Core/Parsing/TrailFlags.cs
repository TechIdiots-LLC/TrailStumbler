using System.Text.Json;
using System.Text.RegularExpressions;

namespace TrailStumbler.Core.Parsing;

/// <summary>What a route is ridden on, for which trails it may use.</summary>
public enum TrailVehicle
{
    /// <summary>Any trail not closed (on foot, by bike, or not said).</summary>
    Any,
    Motorcycle,
    /// <summary>An ATV, 50 inches wide.</summary>
    Atv,
    /// <summary>A UTV (side-by-side), up to 60 inches wide.</summary>
    Utv,
}

/// <summary>Who may use a trail, when its data says: closed to all, closed to motorcycles, only so wide.</summary>
public readonly record struct TrailAccess(bool Closed, bool NoMotorcycles, int? MaxWidthInches)
{
    /// <summary>Whether a route ridden on <paramref name="vehicle"/> may use the trail.</summary>
    public bool Allows(TrailVehicle vehicle) => !Closed && vehicle switch
    {
        TrailVehicle.Motorcycle => !NoMotorcycles,
        TrailVehicle.Atv => MaxWidthInches is not < 50,
        TrailVehicle.Utv => MaxWidthInches is not < 60,
        _ => true,
    };
}

/// <summary>
/// Who may use a trail, only as far as its data says so plainly; any other trail is open to every vehicle (trails
/// come from anyone's GeoJSON, KML or GPX). Saved on a line feature as the properties <c>closed</c>,
/// <c>no_motorcycles</c> and <c>max_width_in</c>: from those properties when a file already has them, from a
/// known Garmin trail map's line types, or from a description in GPS Trailmasters' words ("Closed ATV Trail",
/// "UTV/ATV 60 Inches, No Motorcycles", "ATV 50 Inches and Motorcycles"). A trail's name is never read for them:
/// "Closed Loop" is a name.
/// </summary>
public static partial class TrailFlags
{
    public const string Closed = "closed";
    public const string NoMotorcycles = "no_motorcycles";
    public const string MaxWidth = "max_width_in";

    [GeneratedRegex(@"^\s*closed\b", RegexOptions.IgnoreCase)] private static partial Regex ClosedWords();
    [GeneratedRegex(@"\bno\s+motorcycles?\b", RegexOptions.IgnoreCase)] private static partial Regex NoMotorcycleWords();
    [GeneratedRegex(@"\b(\d{2})\s*inch(?:es)?\b", RegexOptions.IgnoreCase)] private static partial Regex Width();

    /// <summary>What a trail's description says: it begins "Closed", says "No Motorcycles", or gives "NN Inches".</summary>
    public static TrailAccess FromDescription(string? description)
    {
        var text = description ?? "";
        var width = Width().Match(text);
        return new TrailAccess(ClosedWords().IsMatch(text), NoMotorcycleWords().IsMatch(text),
            width.Success ? int.Parse(width.Groups[1].Value) : null);
    }

    /// <summary>What's saved in a feature's properties.</summary>
    public static TrailAccess FromProperties(string? propertiesJson)
    {
        if (string.IsNullOrEmpty(propertiesJson)) return default;
        try
        {
            using var d = JsonDocument.Parse(propertiesJson);
            var root = d.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return default;
            bool Flag(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
            int? width = root.TryGetProperty(MaxWidth, out var w) && w.ValueKind == JsonValueKind.Number &&
                         w.TryGetInt32(out var inches) ? inches : null;
            return new TrailAccess(Flag(Closed), Flag(NoMotorcycles), width);
        }
        catch (JsonException) { return default; }
    }

    /// <summary>Writes the access properties a trail has (none when it's open to all).</summary>
    public static void Write(Utf8JsonWriter w, TrailAccess access)
    {
        if (access.Closed) w.WriteBoolean(Closed, true);
        if (access.NoMotorcycles) w.WriteBoolean(NoMotorcycles, true);
        if (access.MaxWidthInches is { } inches) w.WriteNumber(MaxWidth, inches);
    }
}
