using System.Globalization;
using System.Text;
using System.Text.Json;
using TrailStumbler.Core.Models;

namespace TrailStumbler.Core.Parsing.Garmin;

/// <summary>
/// RGN subfile: decode a subdivision's points, polylines and polygons into
/// <see cref="ParsedFeature"/>s. Ported from GarminBridge's <c>rgn.py</c>.
///
/// Record layouts and the coordinate bitstream (bits_per_coord + sign header)
/// are from the imgdecode reference. The number of trailing points is computed
/// exactly as (8*bitstream_bytes - sign_bits)/(blong+blat) rather than "read
/// until bits run out" — this eliminates the stray trailing vertex that
/// otherwise escapes the tile bounds (~0.04% of vertices).
/// </summary>
internal sealed class RgnSubfile
{
    private readonly byte[] _data;
    public int DataOff { get; }
    public int DataLen { get; }

    public RgnSubfile(byte[] data)
    {
        _data = data;
        DataOff = (int)ByteOps.U32(data, 0x15);
        DataLen = (int)ByteOps.U32(data, 0x19);
    }

    private static int BaseBits(int nib) => 2 + (nib <= 9 ? nib : 2 * nib - 9);

    // -- geometry ------------------------------------------------------
    private static List<(double lon, double lat)> PolylinePoints(
        TreSubfile.Subdivision sub, int dx0, int dy0, bool extra, int bstreamInfo,
        byte[] data, int bstreamOff, int bstreamLen)
    {
        int shift = 24 - sub.Bits;
        var br = new BitReader(data, bstreamOff, bstreamLen);
        int xSame = br.Get(1);
        int xNeg = xSame != 0 ? br.Get(1) : 0;
        int ySame = br.Get(1);
        int yNeg = ySame != 0 ? br.Get(1) : 0;
        int sbits = 2 + (xSame != 0 ? 1 : 0) + (ySame != 0 ? 1 : 0);
        int blong = BaseBits(bstreamInfo & 0xF) + (xSame != 0 ? 0 : 1) + (extra ? 1 : 0);
        int blat = BaseBits((bstreamInfo >> 4) & 0xF) + (ySame != 0 ? 0 : 1) + (extra ? 1 : 0);

        int Delta(int bits, bool variable, int neg)
        {
            int v = br.Get(bits);
            if (variable)
            {
                if ((v & (1 << (bits - 1))) != 0) v -= 1 << bits;
                return v;
            }
            return neg != 0 ? -v : v;
        }

        long lng = sub.CLng + ((long)dx0 << shift);
        long lat = sub.CLat + ((long)dy0 << shift);
        var pts = new List<(double, double)> { (lng * ByteOps.CoordUnit, lat * ByteOps.CoordUnit) };
        int n = (blong + blat) != 0 ? (8 * bstreamLen - sbits) / (blong + blat) : 0;
        for (int i = 0; i < n; i++)
        {
            lng += (long)Delta(blong, xSame == 0, xNeg) << shift;
            lat += (long)Delta(blat, ySame == 0, yNeg) << shift;
            pts.Add((lng * ByteOps.CoordUnit, lat * ByteOps.CoordUnit));
        }
        return pts;
    }

    private IEnumerable<ParsedFeature> DecodePolys(
        int o, int end, TreSubfile.Subdivision sub, bool isLine,
        LabelSubfile labels, HashSet<int>? typeFilter, HashSet<int>? excludeTypes)
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

            string name = inNet ? "" : labels.Get(lblOff);
            string garminType = $"0x{code:x2}";

            var pf = new ParsedFeature
            {
                GeometryType = isLine ? "LineString" : "Polygon",
                PropertiesJson = BuildLineProps(garminType, sub.Zoom, name, isLine),
                Name = name,
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
            int lblOff = pinfo & 0x1FFFFF;
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
        HashSet<int>? cloudPointTypes = null, HashSet<int>? excludePolygonTypes = null)
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
            foreach (var f in DecodePolys(opline, end, sub, true, labels, typeFilter, excludeTypes: null)) yield return f;
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

    private static string BuildLineProps(string garminType, int zoom, string name, bool isLine)
    {
        var sb = new StringBuilder();
        sb.Append("{\"garmin_type\":\"").Append(garminType).Append("\",\"zoom\":").Append(zoom);
        if (name.Length > 0) sb.Append(",\"name\":").Append(JsonSerializer.Serialize(name));
        if (isLine)
        {
            var (cls, color) = GarminTypeClasses.ClassifyLine(garminType);
            sb.Append(",\"class\":").Append(JsonSerializer.Serialize(cls));
            // Emit the simplestyle "stroke" key the map paint expression reads, so
            // each trail type renders in its own colour (same path as KML import).
            sb.Append(",\"stroke\":\"").Append(color).Append('"');
        }
        sb.Append('}');
        return sb.ToString();
    }
}
