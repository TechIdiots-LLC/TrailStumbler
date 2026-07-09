using System.Text;

namespace TrailStumbler.Core.Parsing.Garmin;

/// <summary>
/// IMG container: parse the FAT and reassemble subfiles from 8 KB blocks.
/// Ported from GarminBridge's <c>imgfile.py</c>.
///
/// A classic Garmin .img is a fake-FAT disk image. Directory entries live at
/// offset 0x600, 512 bytes each, flag byte 0x01. Each entry names a subfile
/// (8-char name + 3-char type e.g. RGN/TRE/LBL/NET/NOD/TYP), its size, a part
/// number (subfiles &gt; 240 blocks span multiple entries) and a list of 16-bit
/// block numbers. Block size for these maps is 8192 bytes.
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
        long off = FatStart;
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

    public byte[]? ReadSubfile(string name, string typ)
        => _subfiles.TryGetValue((name, typ), out var sf) ? sf.Read(this) : null;

    public void Dispose() => _fh.Dispose();
}
