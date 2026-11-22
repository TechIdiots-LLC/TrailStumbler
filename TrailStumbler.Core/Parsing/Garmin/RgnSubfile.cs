using System.Globalization;
using System.Text;
using System.Text.Json;
using TrailStumbler.Core.Models;

namespace TrailStumbler.Core.Parsing.Garmin;

/// <summary>
/// RGN subfile: decode a subdivision's points, polylines and polygons into
/// <see cref="ParsedFeature"/>s. Ported from GarminBridge's <c>rgn.py</c>.
///
/// Record layouts are from the imgdecode reference; the coordinate bitstream follows mkgmap's LinePreparer (the
/// writer), which GarminBridge's decoder misread for routable lines and large steps (see PolylinePoints).
/// </summary>
internal sealed class RgnSubfile
{
    private readonly byte[] _data;
    public int DataOff { get; }
    public int DataLen { get; }

    /// <summary>The make of map this is, for what its line types mean.</summary>
    public GarminProduct Product { get; init; }

    public RgnSubfile(byte[] data)
    {
        _data = data;
        DataOff = (int)ByteOps.U32(data, 0x15);
        DataLen = (int)ByteOps.U32(data, 0x19);
    }

    private static int BaseBits(int nib) => 2 + (nib <= 9 ? nib : 2 * nib - 9);

    // -- geometry ------------------------------------------------------
    /// <summary>
    /// A polyline's or polygon's points from its bitstream, as mkgmap's LinePreparer writes it: a sign header for
    /// longitude then latitude (1 bit "all the same sign", then that sign when set); when the record's extra-bit flag
    /// is set (a routable line), one extra bit for the first point; then for each further point its longitude and
    /// latitude deltas and, with the flag, one extra bit (the point is a routing node). A delta of a varying sign is
    /// read with <see cref="BitReader.SGet2"/>, whose escapes let a large step take several values. Points run until
    /// the bits do, less the byte's padding.
    /// </summary>
    internal static List<(double lon, double lat)> PolylinePoints(
        TreSubfile.Subdivision sub, int dx0, int dy0, bool extra, int bstreamInfo,
        byte[] data, int bstreamOff, int bstreamLen)
    {
        int shift = 24 - sub.Bits;
        var br = new BitReader(data, bstreamOff, bstreamLen);
        long lng = sub.CLng + ((long)dx0 << shift);
        long lat = sub.CLat + ((long)dy0 << shift);
        var pts = new List<(double, double)> { (lng * ByteOps.CoordUnit, lat * ByteOps.CoordUnit) };
        try
        {
            bool xSame = br.Get(1) != 0;
            bool xNeg = xSame && br.Get(1) != 0;
            bool ySame = br.Get(1) != 0;
            bool yNeg = ySame && br.Get(1) != 0;
            if (extra) br.Get(1);   // the first point's
            int blong = BaseBits(bstreamInfo & 0xF) + (xSame ? 0 : 1);
            int blat = BaseBits((bstreamInfo >> 4) & 0xF) + (ySame ? 0 : 1);
            int perPoint = blong + blat + (extra ? 1 : 0);
            while (br.Remaining >= perPoint)
            {
                int dx = xSame ? (xNeg ? -br.Get(blong) : br.Get(blong)) : br.SGet2(blong);
                int dy = ySame ? (yNeg ? -br.Get(blat) : br.Get(blat)) : br.SGet2(blat);
                if (extra) br.Get(1);
                // The byte's padding reads as a step of nothing: no point there (mkgmap writes none)
                if (dx == 0 && dy == 0) continue;
                lng += (long)dx << shift;
                lat += (long)dy << shift;
                pts.Add((lng * ByteOps.CoordUnit, lat * ByteOps.CoordUnit));
            }
        }
        catch (EndOfStreamException)
        {
            // A last value cut short: the points read so far stand
        }
        return pts;
    }

    private IEnumerable<ParsedFeature> DecodePolys(
        int o, int end, TreSubfile.Subdivision sub, bool isLine,
        LabelSubfile labels, HashSet<int>? typeFilter, HashSet<int>? excludeTypes, NetSubfile? net = null)
    {
        var d = _data;
        while (o < end)
        {
            int typ = d[o]; o += 1;
            bool twoByte = (typ & 0x80) != 0;
            int code = isLine ? (typ & 0x3F) : (typ & 0x7F);
            int lblInfo = d[o] | (d[o + 1] << 8) | (d[o + 2] << 16); o += 3;
            bool extra = (lblInfo & 0x400000) != 0;
            bool inNet = (lblInfo & 0x800000) != 0;
            int lblOff = lblInfo & 0x3FFFFF;
            int dx = ByteOps.S16(d, o); o += 2;
            int dy = ByteOps.S16(d, o); o += 2;
            int blen;
            if (twoByte) { blen = ByteOps.U16(d, o); o += 2; }
            else { blen = d[o]; o += 1; }
            int info = d[o]; o += 1;
            int bstreamOff = o; o += blen;
            if (typeFilter != null && !typeFilter.Contains(code)) continue;
            if (excludeTypes != null && excludeTypes.Contains(code)) continue;

            var pts = PolylinePoints(sub, dx, dy, extra, info, d, bstreamOff, blen);
            if (pts.Count < 2) continue;

            // A routable line's name is in its NET road record
            string name = labels.Get(inNet ? net?.RoadLabel(lblOff) ?? 0 : lblOff);
            string garminType = $"0x{code:x2}";

            var kind = isLine ? GarminTypeClasses.KindOf(Product, garminType) : null;
            var pf = new ParsedFeature
            {
                GeometryType = isLine ? "LineString" : "Polygon",
                PropertiesJson = BuildLineProps(garminType, sub.Zoom, name, isLine, Product, kind),
                Name = name,
                Description = kind?.Kind ?? "",   // as GPS Trailmasters' KMZ describes the trail
            };
            var sb = new StringBuilder();
            if (isLine)
            {
                AppendRing(sb, pts, pf.Bbox, close: false);
            }
            else
            {
                sb.Append('[');
                AppendRing(sb, pts, pf.Bbox, close: true);
                sb.Append(']');
            }
            pf.CoordinatesJson = sb.ToString();
            yield return pf;
        }
    }

    private IEnumerable<ParsedFeature> DecodePoints(
        int o, int end, TreSubfile.Subdivision sub, LabelSubfile labels,
        HashSet<int>? cloudTypes)
    {
        var d = _data;
        int shift = 24 - sub.Bits;
        while (o < end)
        {
            int typ = d[o]; o += 1;
            int pinfo = d[o] | (d[o + 1] << 8) | (d[o + 2] << 16); o += 3;
            bool hasSub = (pinfo & 0x800000) != 0;
            // A POI's label field addresses its POI properties record, which holds the label (mkgmap's Point.write)
            bool isPoi = (pinfo & 0x400000) != 0;
            int lblOff = pinfo & 0x3FFFFF;
            if (isPoi) lblOff = labels.PoiLabel(lblOff);
            int dx = ByteOps.S16(d, o); o += 2;
            int dy = ByteOps.S16(d, o); o += 2;
            int subtype = hasSub ? d[o] : 0;
            if (hasSub) o += 1;

            // Only trust a label whose offset addresses a real label boundary;
            // otherwise it is a mid-string mis-read (fragment) and we show no name.
            // (The check runs after the whole record is consumed so the running
            // offset stays aligned to the next record.)
            string name = labels.IsRealLabel(lblOff) ? labels.Get(lblOff) : "";
            // "Cloud" types (the TrailIntel 0x66 breadcrumb points) are dropped
            // unless they carry a real label — that labelled subset is the genuine
            // named waypoints; the ~99% unlabelled/fragment ones are noise.
            if (cloudTypes != null && cloudTypes.Contains(typ) && name.Length == 0)
                continue;

            double lon = (sub.CLng + ((long)dx << shift)) * ByteOps.CoordUnit;
            double lat = (sub.CLat + ((long)dy << shift)) * ByteOps.CoordUnit;
            string garminType = $"0x{typ:x2}{subtype:x2}";

            var sb = new StringBuilder();
            AppendPos(sb, lon, lat);

            var pf = new ParsedFeature
            {
                GeometryType = "Point",
                CoordinatesJson = sb.ToString(),
                PropertiesJson = BuildPointProps(garminType, sub.Zoom, name),
                Name = name,
            };
            pf.Bbox.Extend(lon, lat);
            yield return pf;
        }
    }

    // -- dispatch ------------------------------------------------------
    public IEnumerable<ParsedFeature> DecodeSubdivision(
        TreSubfile.Subdivision sub, LabelSubfile labels,
        bool wantPoints, bool wantLines, bool wantAreas, HashSet<int>? typeFilter,
        HashSet<int>? cloudPointTypes = null, HashSet<int>? excludePolygonTypes = null, NetSubfile? net = null)
    {
        byte el = sub.Elements;
        if (el == 0 || sub.RgnStart == sub.RgnEnd) yield break;
        var d = _data;
        int soff = DataOff + sub.RgnStart;
        int eoff = DataOff + sub.RgnEnd;
        int nPresent = Bit(el & TreSubfile.ElemPoint) + Bit(el & TreSubfile.ElemIdxPt)
                     + Bit(el & TreSubfile.ElemLine) + Bit(el & TreSubfile.ElemPolygon);
        int olast = soff + 2 * (nPresent - 1);
        int o = soff;
        int opnt = 0, oidx = 0, opline = 0, opgon = 0;

        if ((el & TreSubfile.ElemPoint) != 0) opnt = olast;
        if ((el & TreSubfile.ElemIdxPt) != 0)
        {
            if (opnt != 0) { oidx = ByteOps.U16(d, o) + soff; o += 2; }
            else oidx = olast;
        }
        if ((el & TreSubfile.ElemLine) != 0)
        {
            if (opnt != 0 || oidx != 0) { opline = ByteOps.U16(d, o) + soff; o += 2; }
            else opline = olast;
        }
        if ((el & TreSubfile.ElemPolygon) != 0)
        {
            if (opnt != 0 || oidx != 0 || opline != 0) { opgon = ByteOps.U16(d, o) + soff; o += 2; }
            else opgon = olast;
        }

        if (wantPoints && (el & TreSubfile.ElemPoint) != 0)
        {
            int end = NonZero(oidx, opline, opgon, eoff);
            foreach (var f in DecodePoints(opnt, end, sub, labels, cloudPointTypes)) yield return f;
        }
        if (wantPoints && (el & TreSubfile.ElemIdxPt) != 0)
        {
            int end = NonZero(opline, opgon, eoff);
            foreach (var f in DecodePoints(oidx, end, sub, labels, cloudPointTypes)) yield return f;
        }
        if (wantLines && (el & TreSubfile.ElemLine) != 0)
        {
            int end = NonZero(opgon, eoff);
            foreach (var f in DecodePolys(opline, end, sub, true, labels, typeFilter, excludeTypes: null, net)) yield return f;
        }
        if (wantAreas && (el & TreSubfile.ElemPolygon) != 0)
        {
            foreach (var f in DecodePolys(opgon, eoff, sub, false, labels, null, excludePolygonTypes)) yield return f;
        }
    }

    private static int Bit(int v) => v != 0 ? 1 : 0;

    /// <summary>First non-zero of the candidates (Python's <c>a or b or c</c> chain).</summary>
    private static int NonZero(params int[] candidates)
    {
        foreach (var c in candidates)
            if (c != 0) return c;
        return 0;
    }

    // -- serialization -------------------------------------------------
    private static void AppendRing(
        StringBuilder sb, List<(double lon, double lat)> pts,
        Core.GeoJson.BoundingBox bbox, bool close)
    {
        sb.Append('[');
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0) sb.Append(',');
            AppendPos(sb, pts[i].lon, pts[i].lat);
            bbox.Extend(pts[i].lon, pts[i].lat);
        }
        // Close the polygon ring if the first and last positions differ.
        if (close && (pts[0].lon != pts[^1].lon || pts[0].lat != pts[^1].lat))
        {
            sb.Append(',');
            AppendPos(sb, pts[0].lon, pts[0].lat);
        }
        sb.Append(']');
    }

    private static void AppendPos(StringBuilder sb, double lon, double lat)
        => sb.Append('[').Append(Fmt(lon)).Append(',').Append(Fmt(lat)).Append(']');

    private static string Fmt(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    private static string BuildPointProps(string garminType, int zoom, string name)
    {
        var sb = new StringBuilder();
        sb.Append("{\"garmin_type\":\"").Append(garminType).Append("\",\"zoom\":").Append(zoom);
        if (name.Length > 0) sb.Append(",\"name\":").Append(JsonSerializer.Serialize(name));
        // Emit the "category" key the symbol layer reads for its coloured sprite,
        // derived from the Garmin POI type — same sprite path as the KML import.
        var category = GarminTypeClasses.CategoryForPoint(garminType);
        if (category != null) sb.Append(",\"category\":\"").Append(category).Append('"');
        sb.Append('}');
        return sb.ToString();
    }

    private static string BuildLineProps(string garminType, int zoom, string name, bool isLine,
        GarminProduct product, TrailKind? kind)
    {
        var sb = new StringBuilder();
        sb.Append("{\"garmin_type\":\"").Append(garminType).Append("\",\"zoom\":").Append(zoom);
        if (name.Length > 0) sb.Append(",\"name\":").Append(JsonSerializer.Serialize(name));
        if (kind is not null)
        {
            sb.Append(",\"description\":").Append(JsonSerializer.Serialize(kind.Kind));
            if (kind.Closed) sb.Append(",\"").Append(TrailFlags.Closed).Append("\":true");
            if (kind.NoMotorcycles) sb.Append(",\"").Append(TrailFlags.NoMotorcycles).Append("\":true");
            if (kind.MaxWidthInches is { } inches) sb.Append(",\"").Append(TrailFlags.MaxWidth).Append("\":").Append(inches);
        }
        if (isLine)
        {
            var (cls, color) = GarminTypeClasses.ClassifyLine(product, garminType);
            sb.Append(",\"class\":").Append(JsonSerializer.Serialize(cls));
            // Emit the simplestyle "stroke" key the map paint expression reads, so
            // each trail type renders in its own colour (same path as KML import).
            sb.Append(",\"stroke\":\"").Append(color).Append('"');
        }
        sb.Append('}');
        return sb.ToString();
    }
}
