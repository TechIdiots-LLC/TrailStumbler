using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using TrailStumbler.Core.Models;

namespace TrailStumbler.Core.Parsing;

/// <summary>
/// Parses GPX 1.0/1.1 using local-name matching (namespace URIs differ between
/// versions): wpt â†’ Point, trk â†’ one LineString per trkseg (trkpt times become a
/// "coordTimes" property, togeojson-style), rte â†’ LineString.
/// </summary>
public class GpxParser : IGisParser
{
    public List<ParsedFeature> Parse(Stream stream)
    {
        XDocument doc;
        try { doc = XDocument.Load(stream); }
        catch (Exception ex) { throw new FormatException("Not a readable GPX file.", ex); }

        var root = doc.Root ?? throw new FormatException("Empty GPX document.");
        var results = new List<ParsedFeature>();

        foreach (var wpt in Descendants(root, "wpt"))
            AddPoint(wpt, results);

        foreach (var trk in Descendants(root, "trk"))
        {
            var name = Value(trk, "name");
            var desc = Value(trk, "desc") ?? Value(trk, "cmt");
            foreach (var seg in Descendants(trk, "trkseg"))
                AddLine(Descendants(seg, "trkpt"), name, desc, results);
        }

        foreach (var rte in Descendants(root, "rte"))
            AddLine(Descendants(rte, "rtept"), Value(rte, "name"), Value(rte, "desc"), results);

        return results;
    }

    private static void AddPoint(XElement wpt, List<ParsedFeature> results)
    {
        if (!TryCoords(wpt, out double lon, out double lat)) return;
        var ele = ParseDouble(Value(wpt, "ele"));

        var pf = new ParsedFeature { GeometryType = "Point" };
        pf.CoordinatesJson = ele.HasValue
            ? FormattableString.Invariant($"[{lon},{lat},{ele.Value}]")
            : FormattableString.Invariant($"[{lon},{lat}]");
        pf.Bbox.Extend(lon, lat);

        pf.Name = Value(wpt, "name") ?? "";
        pf.Description = DescriptionCleaner.CleanForDisplay(Value(wpt, "desc") ?? Value(wpt, "cmt") ?? "");
        pf.PropertiesJson = BuildProps(pf.Name, pf.Description,
            ("sym", Value(wpt, "sym")), ("time", Value(wpt, "time")));
        results.Add(pf);
    }

    private static void AddLine(IEnumerable<XElement> points, string? name, string? desc, List<ParsedFeature> results)
    {
        var coords = new StringBuilder("[");
        var times = new List<string>();
        var pf = new ParsedFeature { GeometryType = "LineString" };
        int count = 0;

        foreach (var pt in points)
        {
            if (!TryCoords(pt, out double lon, out double lat)) continue;
            if (count > 0) coords.Append(',');
            var ele = ParseDouble(Value(pt, "ele"));
            coords.Append(ele.HasValue
                ? FormattableString.Invariant($"[{lon},{lat},{ele.Value}]")
                : FormattableString.Invariant($"[{lon},{lat}]"));
            pf.Bbox.Extend(lon, lat);

            var t = Value(pt, "time");
            if (t is not null) times.Add(t);
            count++;
        }
        if (count < 2) return;
        coords.Append(']');

        pf.CoordinatesJson = coords.ToString();
        pf.Name = name ?? "";
        pf.Description = DescriptionCleaner.CleanForDisplay(desc ?? "");
        pf.PropertiesJson = BuildProps(pf.Name, pf.Description,
            ("coordTimes", times.Count == count ? times : null));
        results.Add(pf);
    }

    private static string BuildProps(string name, string desc, params (string Key, object? Value)[] extras)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            if (name.Length > 0) w.WriteString("name", name);
            if (desc.Length > 0) w.WriteString("description", desc);
            foreach (var (key, value) in extras)
            {
                switch (value)
                {
                    case string s when s.Length > 0:
                        w.WriteString(key, s);
                        break;
                    case List<string> list when list.Count > 0:
                        w.WriteStartArray(key);
                        foreach (var item in list) w.WriteStringValue(item);
                        w.WriteEndArray();
                        break;
                }
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static bool TryCoords(XElement el, out double lon, out double lat)
    {
        lon = lat = 0;
        return double.TryParse(el.Attribute("lon")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lon)
            && double.TryParse(el.Attribute("lat")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lat);
    }

    private static double? ParseDouble(string? s)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static IEnumerable<XElement> Descendants(XElement el, string localName)
        => el.Descendants().Where(e => e.Name.LocalName == localName);

    private static string? Value(XElement el, string localName)
        => el.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim();
}
