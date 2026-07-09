namespace TrailStumbler.Core.Parsing.Garmin;

/// <summary>
/// Trail class + colour mapping for Garmin line type codes. Ported from
/// GarminBridge's <c>typ.py</c> and extended with the codes actually observed in
/// the TrailIntel ATV maps (ne/mw/se/sw).
///
/// A full binary TYP parser (the map's own colours/bitmaps) is future work; the
/// TYP in these maps is tiny and stores styling, not readable class names. Until
/// then we ship a curated map of the codes seen in the ATV maps, and any code not
/// in the table still gets a distinct, stable colour from <see cref="Palette"/> so
/// different trail types always render apart.
///
/// The colour is surfaced as the simplestyle <c>stroke</c> property on each line
/// feature, so trail lines are coloured through the exact same MapLibre
/// <c>["case", ["has","stroke"], ["get","stroke"], layerColour]</c> paint
/// expression as the GPS Trail Masters KML import.
/// </summary>
internal static class GarminTypeClasses
{
    // Observed line (trail) type codes across the ATV maps. Names are best guesses
    // for display/grouping; colours are chosen to be distinct and legible. Adjust
    // as the TYP legend is confirmed.
    private static readonly Dictionary<string, (string Name, string Color)> DefaultLineClasses = new()
    {
        ["0x0d"] = ("trail_primary",   "#E2571E"), // most prominent trail
        ["0x0e"] = ("trail_atv",       "#1B7837"), // green
        ["0x0f"] = ("trail",           "#F0A81A"), // amber — the common trail code
        ["0x10"] = ("trail_road",      "#2166AC"), // blue
        ["0x11"] = ("trail_secondary", "#762A83"), // purple
        ["0x12"] = ("trail_connector", "#8A4B1E"), // brown
        ["0x22"] = ("trail_boundary",  "#4D4D4D"), // grey — non-trail line (boundary/other)
    };

    // Distinct fallback colours for any line code not in the curated table, keyed
    // deterministically off the code so a given type is always the same colour.
    private static readonly string[] Palette =
    {
        "#E41A1C", "#377EB8", "#4DAF4A", "#984EA3", "#FF7F00",
        "#A65628", "#F781BF", "#00CED1", "#999999",
    };

    /// <summary>
    /// Map a point's Garmin type (e.g. "0x2f01") to one of the sprite categories the
    /// map already understands from the KML import (see <c>MapViewModel.SymbolLayout</c>):
    /// fuel / food / lodging / parking / camping / scenic_view / restroom / atv_club.
    /// Emitted as the <c>category</c> property so Garmin POIs get the same coloured
    /// sprites as GPS Trail Masters points. Confirmed against the ATV maps' labels:
    /// 0x2f01 = gas ("CHEVRON GAS…"), 0x2a00 = food ("… /DQ", "… RESTAURANT").
    /// Types whose meaning isn't confirmed return null → the generic circle sprite.
    /// </summary>
    public static string? CategoryForPoint(string garminType)
    {
        if (!garminType.StartsWith("0x") ||
            !int.TryParse(garminType.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out int code))
            return null;
        int hi = (code >> 8) & 0xFF;   // Garmin POI group (high type byte)
        return code switch
        {
            0x2f01 => "fuel",
            _ => hi switch
            {
                0x2a => "food",       // Food & Drink
                0x2b => "lodging",    // Hotels & Lodging
                _ => null,            // 0x2c waypoints / 0x2f other: unconfirmed → circle
            },
        };
    }

    /// <returns>(class name, non-null stroke colour) for a line type code such as "0x0f".</returns>
    public static (string Name, string Color) ClassifyLine(string garminType)
    {
        if (DefaultLineClasses.TryGetValue(garminType, out var v)) return v;
        // Stable fallback: derive an index from the hex code so unknown types are
        // still visually distinct rather than all defaulting to the layer colour.
        int code = 0;
        if (garminType.StartsWith("0x") &&
            int.TryParse(garminType.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var parsed))
            code = parsed;
        return (garminType, Palette[Math.Abs(code) % Palette.Length]);
    }
}
