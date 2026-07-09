namespace TrailStumbler.Core.Parsing.Garmin;

/// <summary>
/// TRE subfile: map bounding box, map levels (TRE1) and subdivisions (TRE2).
/// Ported from GarminBridge's <c>tre.py</c>.
///
/// Subdivision records are 16 bytes (14 for the most-detailed / last level):
///   u24 rgn_offset | u8 elements | s24 centre_lng | s24 centre_lat |
///   u16 width(&amp;0x7fff) | u16 height(&amp;0x7fff) | [u16 next-level-idx].
/// The <c>elements</c> bits: 0x10 points, 0x20 indexed points, 0x40 polylines,
/// 0x80 polygons.
/// </summary>
internal sealed class TreSubfile
{
    public const byte ElemPoint = 0x10;
    public const byte ElemIdxPt = 0x20;
    public const byte ElemLine = 0x40;
    public const byte ElemPolygon = 0x80;

    internal sealed class Subdivision
    {
        public int Level;
        public int Zoom;
        public int Bits;
        public int RgnStart;
        public int RgnEnd;
        public byte Elements;
        public int CLng;
        public int CLat;
    }

    private readonly byte[] _data;

    /// <summary>(minLon, minLat, maxLon, maxLat).</summary>
    public readonly (double MinLon, double MinLat, double MaxLon, double MaxLat) Bbox;

    private readonly List<(int zoom, int bits, int nsub)> _levels;
    public List<Subdivision> Subdivisions { get; }

    public TreSubfile(byte[] data)
    {
        _data = data;
        double n = ByteOps.S24(data, 0x15) * ByteOps.CoordUnit;
        double e = ByteOps.S24(data, 0x18) * ByteOps.CoordUnit;
        double s = ByteOps.S24(data, 0x1B) * ByteOps.CoordUnit;
        double w = ByteOps.S24(data, 0x1E) * ByteOps.CoordUnit;
        Bbox = (Math.Min(w, e), Math.Min(s, n), Math.Max(w, e), Math.Max(s, n));
        _levels = ReadLevels();
        Subdivisions = ReadSubdivisions();
    }

    private List<(int, int, int)> ReadLevels()
    {
        var d = _data;
        int off = (int)ByteOps.U32(d, 0x21);
        int size = (int)ByteOps.U32(d, 0x25);
        var outp = new List<(int, int, int)>();
        for (int o = off; o + 4 <= off + size; o += 4)
        {
            int zoom = d[o] & 0x0F;
            int bits = d[o + 1];
            int nsub = ByteOps.U16(d, o + 2);
            outp.Add((zoom, bits, nsub));
        }
        return outp;
    }

    private List<Subdivision> ReadSubdivisions()
    {
        var d = _data;
        int o = (int)ByteOps.U32(d, 0x29);   // TRE2 offset
        var subs = new List<Subdivision>();
        for (int li = 0; li < _levels.Count; li++)
        {
            var (zoom, bits, nsub) = _levels[li];
            int reclen = li == _levels.Count - 1 ? 14 : 16;
            for (int k = 0; k < nsub; k++)
            {
                subs.Add(new Subdivision
                {
                    Level = li,
                    Zoom = zoom,
                    Bits = bits,
                    RgnStart = d[o] | (d[o + 1] << 8) | (d[o + 2] << 16),
                    Elements = d[o + 3],
                    CLng = ByteOps.S24(d, o + 4),
                    CLat = ByteOps.S24(d, o + 7),
                });
                o += reclen;
            }
        }
        return subs;
    }

    /// <summary>RGN object data for subdivision i spans [rgn_start, next.rgn_start).</summary>
    public void Finalize(int rgnDataLen)
    {
        for (int i = 0; i < Subdivisions.Count; i++)
            Subdivisions[i].RgnEnd = i + 1 < Subdivisions.Count
                ? Subdivisions[i + 1].RgnStart
                : rgnDataLen;
    }
}
