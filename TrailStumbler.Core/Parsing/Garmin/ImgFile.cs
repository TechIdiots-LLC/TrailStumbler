using System.Text;

namespace TrailStumbler.Core.Parsing.Garmin;

/// <summary>
/// IMG container: parse the FAT and reassemble subfiles from blocks. Ported from GarminBridge's <c>imgfile.py</c>,
/// with the directory and block size read from the header rather than assumed.
///
/// A classic Garmin .img is a fake-FAT disk image. The directory starts at the sector the header's byte 0x40 gives
/// (0x400 in a single map's file, the first entry the header's own; 0x1000 in a combined gmapsupp-style one), 512
/// bytes an entry, flag byte 0x01. Each entry names a subfile (8-char name + 3-char type e.g. RGN/TRE/LBL/NET/NOD/
/// TYP/MPS), its size, a part number (subfiles spanning many blocks take several entries) and a list of 16-bit block
/// numbers. The block size is 1 &lt;&lt; (E1 + E2) from the header (0x61/0x62): 1024 to 8192 in the maps seen.
///
/// Encrypted/XOR-obfuscated maps are not supported (the reference decoder reads
/// the header and FAT verbatim); those are rejected by the format sniffer.
/// </summary>
internal sealed class ImgFile : IDisposable
{
    private const int FatStart = 0x600;
    private const int FatEntry = 512;

    // Logical block size is not fixed: it is 1 << (E1 + E2) from the header
    // (offsets 0x61/0x62). Small maps use 1024-byte blocks, large routable maps
    // 8192 — hardcoding either misreads the other.
    private readonly int _blockSize;
    private readonly long _fatStart;

    private sealed class Subfile
    {
        public readonly string Name;
        public readonly string Type;
        public readonly int Size;
        public readonly List<(int part, List<int> blocks)> Parts = new();

        public Subfile(string name, string type, int size)
        {
            Name = name;
            Type = type;
            Size = size;
        }

        public byte[] Read(ImgFile img)
        {
            using var ms = new MemoryStream();
            foreach (var (_, blocks) in Parts.OrderBy(p => p.part))
                foreach (var b in blocks)
                    ms.Write(img.Block(b));
            var all = ms.ToArray();
            if (all.Length <= Size) return all;
            var trimmed = new byte[Size];
            Array.Copy(all, trimmed, Size);
            return trimmed;
        }
    }

    private readonly Stream _fh;
    private readonly Dictionary<(string, string), Subfile> _subfiles = new();
    private readonly Dictionary<string, HashSet<string>> _tiles = new();
    private readonly byte[] _blockBuf;

    public string Label { get; }

    public ImgFile(Stream stream)
    {
        if (!stream.CanSeek)
            throw new FormatException("Garmin .img import requires a seekable stream.");
        _fh = stream;
        var hdr = Read(0x61, 2);   // E1, E2 block-size exponents
        int e1 = hdr.Length > 0 ? hdr[0] : 9;
        int e2 = hdr.Length > 1 ? hdr[1] : 0;
        _blockSize = 1 << (e1 + e2);
        _blockBuf = new byte[_blockSize];
        // The directory starts where the header says (byte 0x40, in 512-byte sectors): 0x400 in a single map's
        // file, later in a combined one (0x1000 in GPS Trailmasters' New England gmapsupp-style file). 0x600 was
        // right only by luck, as a single map's first entry (at 0x400) is the header's own.
        int dirSector = Read(0x40, 1) is { Length: 1 } d ? d[0] : 0;
        _fatStart = dirSector > 0 ? dirSector * 512L : FatStart;
        Label = ReadMapsetLabel();
        ParseFat();
    }

    // -- low level ------------------------------------------------------
    private ReadOnlySpan<byte> Block(int n)
    {
        _fh.Seek((long)n * _blockSize, SeekOrigin.Begin);
        int read = ReadFully(_blockBuf, _blockBuf.Length);
        return _blockBuf.AsSpan(0, read);
    }

    private byte[] Read(long off, int n)
    {
        _fh.Seek(off, SeekOrigin.Begin);
        var buf = new byte[n];
        int read = ReadFully(buf, n);
        return read == n ? buf : buf[..read];
    }

    private int ReadFully(byte[] buf, int n)
    {
        int total = 0;
        while (total < n)
        {
            int r = _fh.Read(buf, total, n - total);
            if (r == 0) break;
            total += r;
        }
        return total;
    }

    private string ReadMapsetLabel()
    {
        // Human-readable mapset name embedded in the disk header (offset 0x49).
        var raw = Read(0x49, 0x1E);
        int end = Array.IndexOf(raw, (byte)0);
        if (end < 0) end = raw.Length;
        return Latin1(raw, 0, end).Trim();
    }

    private void ParseFat()
    {
        long off = _fatStart;
        while (true)
        {
            var e = Read(off, FatEntry);
            if (e.Length < FatEntry || e[0] != 1) break;

            string name = Latin1(e, 1, 8).TrimEnd('\0', ' ');
            string typ = Latin1(e, 9, 3).TrimEnd('\0', ' ');
            int size = (int)ByteOps.U32(e, 0x0C);
            int part = ByteOps.U16(e, 0x10);

            var blocks = new List<int>();
            for (int i = 0x20; i < FatEntry; i += 2)
            {
                int b = ByteOps.U16(e, i);
                if (b == 0xFFFF) break;
                blocks.Add(b);
            }

            var key = (name, typ);
            if (!_subfiles.TryGetValue(key, out var sf))
            {
                sf = new Subfile(name, typ, size);
                _subfiles[key] = sf;
                if (name.Length > 0 && typ.Length > 0)
                {
                    if (!_tiles.TryGetValue(name, out var set))
                        _tiles[name] = set = new HashSet<string>();
                    set.Add(typ);
                }
            }
            sf.Parts.Add((part, blocks));
            off += FatEntry;
        }
    }

    private static string Latin1(byte[] d, int start, int len)
    {
        int n = Math.Min(len, d.Length - start);
        return n <= 0 ? "" : Encoding.Latin1.GetString(d, start, n);
    }

    // -- public ---------------------------------------------------------
    /// <summary>Names of tiles that carry geometry (have both TRE and RGN).</summary>
    public List<string> MapTiles()
    {
        var names = _tiles
            .Where(kv => kv.Value.Contains("TRE") && kv.Value.Contains("RGN"))
            .Select(kv => kv.Key)
            .ToList();
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>
    /// Each map's description by its tile name ("00190950" → "Trail Map"), from the MapSource MPS record a
    /// combined (gmapsupp-style) file carries; empty when there's none. An MPS "L" record is: 'L', a 16-bit length,
    /// the product and family ids (16-bit each), the map number (32-bit, the tile name in decimal), then the series
    /// name, the map's description and the area, each NUL-terminated.
    /// </summary>
    public IReadOnlyDictionary<string, string> MapDescriptions()
    {
        var names = new Dictionary<string, string>();
        _series.Clear();
        foreach (var ((_, typ), sf) in _subfiles)
        {
            if (typ != "MPS") continue;
            var d = sf.Read(this);
            for (int o = 0; o + 3 <= d.Length;)
            {
                char kind = (char)d[o];
                int len = ByteOps.U16(d, o + 1);
                int body = o + 3;
                if (len <= 0 || body + len > d.Length) break;
                if (kind == 'L' && len >= 8)
                {
                    uint map = ByteOps.U32(d, body + 4);
                    int p = body + 8;
                    string Next()
                    {
                        int end = Array.IndexOf(d, (byte)0, p, body + len - p);
                        if (end < 0) end = body + len;
                        var s = Latin1(d, p, end - p);
                        p = end + 1;
                        return s;
                    }
                    _series.Add(Next());          // the series ("GPS Trailmasters ATV New England V12.00")
                    names[map.ToString("D8")] = Next();
                }
                o = body + len;
            }
        }
        return names;
    }

    private readonly HashSet<string> _series = new();

    /// <summary>The series names of the file's maps, read by <see cref="MapDescriptions"/>.</summary>
    public IReadOnlyCollection<string> MapSeries => _series;

    public byte[]? ReadSubfile(string name, string typ)
        => _subfiles.TryGetValue((name, typ), out var sf) ? sf.Read(this) : null;

    public void Dispose() => _fh.Dispose();
}
