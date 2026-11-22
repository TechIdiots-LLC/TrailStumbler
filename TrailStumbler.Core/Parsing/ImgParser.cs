using TrailStumbler.Core.Models;
using TrailStumbler.Core.Parsing.Garmin;

namespace TrailStumbler.Core.Parsing;

/// <summary>
/// Parses a classic Garmin <c>.img</c> map into <see cref="ParsedFeature"/>s by
/// decoding every map tile's RGN geometry (points, polylines, polygons) to
/// GeoJSON-style coordinates. This is a C# port of the GarminBridge Python
/// decoder pipeline: ImgFile (FAT/subfiles) → TRE (levels/subdivisions)
/// → RGN (lines/points/polygons) → LBL (labels) → typ (classes).
///
/// The mixed families flow through <c>ImportService</c>'s existing per-family
/// splitting, so an .img yields separate "(trails)", "(points)" and "(areas)"
/// layers automatically.
/// </summary>
public class ImgParser : IGisParser
{
    /// <summary>Emit only the most-detailed zoom level (subdivision zoom 0),
    /// avoiding the generalized copies of the same geometry at coarser levels.</summary>
    private readonly bool _detailOnly;

    /// <summary>When true, cut the background clutter the TrailIntel ATV maps bake in:
    /// the dense <c>0x66</c> breadcrumb point cloud (one marker per trail vertex) is
    /// reduced to only its genuinely-labelled members (the real named waypoints —
    /// e.g. "NORTHERN TRAILHEAD", "TOWPATH RESTAURANT"), and the background grid
    /// polygons (area type <c>0x4b</c>) are dropped. Real POIs (gas/food, types
    /// 0x2a/0x2f) and the trail polylines are always kept.</summary>
    private readonly bool _skipBackgroundJunk;

    /// <summary>Dense "cloud" point <b>type bytes</b>: kept only when the point has a
    /// real label (see <c>LabelSubfile.IsRealLabel</c>), otherwise dropped as noise.</summary>
    private static readonly HashSet<int> CloudPointTypes = new() { 0x66 };
    /// <summary>Polygon <b>type codes</b> (typ &amp; 0x7F) to drop outright.</summary>
    private static readonly HashSet<int> JunkPolygonTypes = new() { 0x4b };

    /// <summary>Decode a combined file's background map too, its features in <see cref="ParsedFeature.BackgroundGroup"/>.</summary>
    private readonly bool _includeBackground;

    public ImgParser(bool detailOnly = true, bool skipBackgroundJunk = true, bool includeBackground = false)
    {
        _detailOnly = detailOnly;
        _skipBackgroundJunk = skipBackgroundJunk;
        _includeBackground = includeBackground;
    }

    /// <summary>
    /// The maps in a combined file that are its maker's background map, not trails: one described as a background
    /// map or basemap (GPS Trailmasters' "Background Map"), and one with mkgmap's default description, "OSM street
    /// map" (TrailIntel's 60 street-map tiles, beside its trail map named for the product). A map of one's own built
    /// with mkgmap's defaults has that description too, so those are left out only when another map is kept.
    /// </summary>
    internal static HashSet<string> BackgroundMaps(IEnumerable<string> tiles, IReadOnlyDictionary<string, string> described)
    {
        var named = tiles.Select(t => (Tile: t, What: described.GetValueOrDefault(t, ""))).ToList();
        var skip = named.Where(t => t.What.Contains("background", StringComparison.OrdinalIgnoreCase) ||
                                    t.What.Contains("basemap", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Tile).ToHashSet();
        var streetMaps = named.Where(t => !skip.Contains(t.Tile) &&
                                          t.What.Equals("OSM street map", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Tile).ToList();
        if (named.Count - skip.Count > streetMaps.Count) skip.UnionWith(streetMaps);
        return skip;
    }

    /// <summary>How many of the file's maps are its maker's background map (read from its directory only).</summary>
    public static int BackgroundMapCount(Stream stream)
    {
        using var img = new ImgFile(stream);
        return BackgroundMaps(img.MapTiles(), img.MapDescriptions()).Count;
    }

    public List<ParsedFeature> Parse(Stream stream)
    {
        var results = new List<ParsedFeature>();
        using var img = new ImgFile(stream);
        var tiles = img.MapTiles();
        // A combined file's background map (roads, water, towns: millions of features) isn't trails
        var background = _skipBackgroundJunk ? BackgroundMaps(tiles, img.MapDescriptions()) : [];
        foreach (var name in tiles)
        {
            bool isBackground = background.Contains(name);
            if (isBackground && !_includeBackground) continue;
            var treData = img.ReadSubfile(name, "TRE");
            var rgnData = img.ReadSubfile(name, "RGN");
            if (treData is null || rgnData is null) continue;

            var tre = new TreSubfile(treData);
            var rgn = new RgnSubfile(rgnData);
            tre.Finalize(rgn.DataLen);
            var labels = new LabelSubfile(img.ReadSubfile(name, "LBL"));
            var net = new NetSubfile(img.ReadSubfile(name, "NET"));

            foreach (var sub in tre.Subdivisions)
            {
                if (_detailOnly && sub.Zoom != 0) continue;
                foreach (var feat in rgn.DecodeSubdivision(
                             sub, labels, wantPoints: true, wantLines: true,
                             wantAreas: true, typeFilter: null,
                             cloudPointTypes: _skipBackgroundJunk ? CloudPointTypes : null,
                             excludePolygonTypes: _skipBackgroundJunk ? JunkPolygonTypes : null, net: net))
                {
                    if (isBackground) feat.Group = ParsedFeature.BackgroundGroup;
                    results.Add(feat);
                }
            }
        }
        return results;
    }
}
