using System.Globalization;
using System.Text;

namespace TrailStumbler.Core.Parsing.Garmin;

/// <summary>The trail map products whose line types are known (each maker numbers them its own way).</summary>
internal enum GarminProduct { Unknown, GpsTrailmastersAtv, TrailIntelAtv, TrailIntelSnowmobile }

/// <summary>What a line type is on a product's map: its kind (shown as the trail's description), colour, and who may use it.</summary>
internal sealed record TrailKind(string Kind, string Color, bool Closed = false, bool NoMotorcycles = false, int? MaxWidthInches = null);

/// <summary>
/// Trail kinds and POI categories for Garmin type codes. A map's TYP holds only styling, not names, so the line
/// types were matched against GPS Trailmasters' KMZ of the same trails, which names each one (docs in
/// docs/garmin-import.md): the 2023 GPS Trailmasters map and TrailIntel's number the same kinds differently. The
/// snowmobile map's kinds are read from its trails' names ("PROVINCIAL 83", "REGIONAL 303", "DANGER - LAKE …"),
/// with no KMZ to check them by. A map of another make keeps a stable colour per code and no kinds.
///
/// The colour is the simplestyle <c>stroke</c> property the map's line paint reads, as for a KML import.
/// </summary>
internal static class GarminTypeClasses
{
    // GPS Trailmasters' own KMZ colours (KML is aabbggrr): green 60" trails, blue corridors, red 50" trails, black
    // access roads, grey closed trails
    private const string Green = "#008000", Blue = "#0000FF", Red = "#FF0000", Black = "#000000", Grey = "#808080";

    private static readonly Dictionary<(GarminProduct, int), TrailKind> Kinds = new()
    {
        [(GarminProduct.GpsTrailmastersAtv, 0x01)] = new("UTV/ATV 60 Inches and Motorcycles", Green, MaxWidthInches: 60),
        [(GarminProduct.GpsTrailmastersAtv, 0x02)] = new("UTV/ATV 60 Inches, No Motorcycles", Green, NoMotorcycles: true, MaxWidthInches: 60),
        [(GarminProduct.GpsTrailmastersAtv, 0x03)] = new("Corridor", Blue),
        [(GarminProduct.GpsTrailmastersAtv, 0x04)] = new("ATV 50 Inches and Motorcycles", Red, MaxWidthInches: 50),
        [(GarminProduct.GpsTrailmastersAtv, 0x0b)] = new("ATV Access Trail", Black),
        [(GarminProduct.GpsTrailmastersAtv, 0x1d)] = new("Closed ATV Trail", Grey, Closed: true),

        [(GarminProduct.TrailIntelAtv, 0x03)] = new("UTV/ATV 60 Inches and Motorcycles", Green, MaxWidthInches: 60),
        [(GarminProduct.TrailIntelAtv, 0x02)] = new("UTV/ATV 60 Inches, No Motorcycles", Green, NoMotorcycles: true, MaxWidthInches: 60),
        [(GarminProduct.TrailIntelAtv, 0x04)] = new("Corridor", Blue),
        [(GarminProduct.TrailIntelAtv, 0x07)] = new("ATV 50 Inches and Motorcycles", Red, MaxWidthInches: 50),
        [(GarminProduct.TrailIntelAtv, 0x08)] = new("ATV Access Trail", Black),
        [(GarminProduct.TrailIntelAtv, 0x06)] = new("Closed ATV Trail", Grey, Closed: true),
        [(GarminProduct.TrailIntelAtv, 0x05)] = new("Private Trail", "#8A4B1E"),

        [(GarminProduct.TrailIntelSnowmobile, 0x0b)] = new("Provincial / ITS Trail", "#D32F2F"),
        [(GarminProduct.TrailIntelSnowmobile, 0x0d)] = new("Corridor", "#D32F2F"),
        [(GarminProduct.TrailIntelSnowmobile, 0x04)] = new("Regional Trail", "#F57C00"),
        [(GarminProduct.TrailIntelSnowmobile, 0x02)] = new("Local / Club Trail", "#1976D2"),
        [(GarminProduct.TrailIntelSnowmobile, 0x07)] = new("Trail", "#7B1FA2"),
        [(GarminProduct.TrailIntelSnowmobile, 0x08)] = new("Access, Private or Unmaintained Trail", Black),
        [(GarminProduct.TrailIntelSnowmobile, 0x03)] = new("Approximate Trail", Grey),
        [(GarminProduct.TrailIntelSnowmobile, 0x06)] = new("Danger / Caution", "#FBC02D"),
        [(GarminProduct.TrailIntelSnowmobile, 0x0c)] = new("Danger / Caution", "#FBC02D"),
    };

    // Distinct colours for a code of an unknown make, keyed off the code so a type is always the same colour
    private static readonly string[] Palette =
    {
        "#E41A1C", "#377EB8", "#4DAF4A", "#984EA3", "#FF7F00",
        "#A65628", "#F781BF", "#00CED1", "#999999",
    };

    /// <summary>The product a map is, from its MapSource names ("TrailIntel Snowmobile Map NE", "GPS Trailmasters ATV New England V12.00").</summary>
    public static GarminProduct ProductOf(IEnumerable<string> mapNames)
    {
        foreach (var n in mapNames)
        {
            bool atv = n.Contains("ATV", StringComparison.OrdinalIgnoreCase);
            if (n.Contains("TrailIntel", StringComparison.OrdinalIgnoreCase))
            {
                if (n.Contains("Snowmobile", StringComparison.OrdinalIgnoreCase)) return GarminProduct.TrailIntelSnowmobile;
                if (atv) return GarminProduct.TrailIntelAtv;
            }
            if (atv && n.Contains("Trailmasters", StringComparison.OrdinalIgnoreCase)) return GarminProduct.GpsTrailmastersAtv;
        }
        return GarminProduct.Unknown;
    }

    /// <summary>The trail maps' catch-all point type (Garmin's generic man-made feature): only the name says what it is.</summary>
    public const string GenericPointType = "0x6400";

    // Words or phrases in a generic point's name, by category; the first match wins, so a fire station or post office
    // inside a store is the former. Matched on whole words, upper case, without accents.
    private static readonly (string Category, string[] Words)[] NameCategories =
    [
        ("fire_station", ["FIRE", "CASERNE", "POMPIERS", "INCENDIE"]),
        ("police", ["POLICE", "RCMP", "SHERIFF", "SURETE", "TROOPER", "TROOPERS"]),
        ("post_office", ["POSTAL", "POST OFFICE", "CANADA POST", "POSTES CANADA", "USPS"]),
        ("town_hall", ["TOWN HALL", "TOWN OFFICE", "CITY HALL", "HOTEL DE VILLE", "MAIRIE", "MUNICIPAL"]),
        ("pharmacy", ["PHARMACY", "PHARMACIE", "DRUG", "DRUGS", "FAMILIPRIX", "PHARMAPRIX", "UNIPRIX", "PROXIM",
            "JEAN COUTU", "CVS", "WALGREENS", "RITE AID"]),
        ("bank", ["ATM", "BANK", "BANQUE", "CAISSE", "DESJARDINS", "CREDIT UNION", "BITCOIN", "CARDTRONICS", "LIBERTYX",
            "CIBC"]),
        ("repair", ["GARAGE", "MECANIQUE", "CARROSSERIE", "DEBOSSELAGE", "PNEUS", "TIRE", "TIRES", "AUTO", "TOWING",
            "REMORQUAGE"]),
        ("laundry", ["LAUNDROMAT", "LAUNDRY", "BUANDERIE"]),
        ("shop", ["HARDWARE", "QUINCAILLERIE", "WALMART", "DEPANNEUR", "STORE", "MARKET", "VARIETY", "SUPPLY",
            "GROCERY", "EPICERIE"]),
    ];

    /// <summary>
    /// A point's Garmin type ("0x2f01") as one of the sprite categories the map knows (see
    /// <c>MapViewModel.SymbolLayout</c>); null for the generic dot. Checked against GPS Trailmasters' KMZ points:
    /// 0x2a food, 0x2f01 fuel, 0x2f0b parking and trailheads, 0x2c04 scenic places, parks and camping. A
    /// <see cref="GenericPointType"/> point is categorised by words in its <paramref name="name"/>.
    /// </summary>
    public static string? CategoryForPoint(string garminType, string name = "")
    {
        if (!TryCode(garminType, out int code)) return null;
        return code switch
        {
            0x2f01 => "fuel",
            0x2f0b => "parking",
            0x2c04 => "scenic_view",
            0x6400 => CategoryFromName(name),
            _ => ((code >> 8) & 0xFF) switch
            {
                0x2a => "food",
                0x2b => "lodging",
                _ => null,
            },
        };
    }

    private static string? CategoryFromName(string name)
    {
        if (name.Length == 0) return null;
        // " WORD WORD " so a phrase matches only on word boundaries
        var text = new StringBuilder(" ");
        foreach (char c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;   // É → E
            if (char.IsLetterOrDigit(c)) text.Append(char.ToUpperInvariant(c));
            else if (text[^1] != ' ') text.Append(' ');
        }
        if (text[^1] != ' ') text.Append(' ');
        var words = text.ToString();
        foreach (var (category, phrases) in NameCategories)
            foreach (var phrase in phrases)
                if (words.Contains($" {phrase} ", StringComparison.Ordinal)) return category;
        return null;
    }

    /// <summary>A line type's kind on the product's map, or null when it isn't known.</summary>
    public static TrailKind? KindOf(GarminProduct product, string garminType) =>
        TryCode(garminType, out int code) && Kinds.TryGetValue((product, code), out var kind) ? kind : null;

    /// <returns>(class name, stroke colour) for a line type: its kind's, else the code and a stable colour.</returns>
    public static (string Name, string Color) ClassifyLine(GarminProduct product, string garminType)
    {
        if (KindOf(product, garminType) is { } kind) return (kind.Kind, kind.Color);
        TryCode(garminType, out int code);
        return (garminType, Palette[Math.Abs(code) % Palette.Length]);
    }

    private static bool TryCode(string garminType, out int code)
    {
        code = 0;
        return garminType.StartsWith("0x") &&
               int.TryParse(garminType.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out code);
    }
}
