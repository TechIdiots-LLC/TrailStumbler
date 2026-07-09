using System.Buffers.Binary;
using System.Text.Json;
using TrailStumbler.Core.Models;
using TrailStumbler.Core.Parsing;
using TrailStumbler.Core.Parsing.Garmin;
using Xunit;

namespace TrailStumbler.Core.Tests;

/// <summary>
/// Covers the deterministic Garmin decoder helpers (mirrors GarminBridge's
/// test_decoder.py) plus an end-to-end decode of a hand-built single-tile .img.
/// A point tile exercises FAT parse → block reassembly → TRE → RGN point decode
/// → coordinate math without needing the polyline bitstream.
/// </summary>
public class GarminHelperTests
{
    [Fact]
    public void BitReaderIsLsbFirst()
    {
        var br = new BitReader(new byte[] { 0b10110001 }, 0, 1);
        Assert.Equal(1, br.Get(1));          // LSB first
        Assert.Equal(0b000, br.Get(3));
        Assert.Equal(0b1011, br.Get(4));
    }

    [Fact]
    public void S24IsSigned()
    {
        Assert.Equal(-0x800000, ByteOps.S24(new byte[] { 0x00, 0x00, 0x80 }, 0));
        Assert.Equal(0x7FFFFF, ByteOps.S24(new byte[] { 0xFF, 0xFF, 0x7F }, 0));
    }

    [Fact]
    public void CoordUnitCovers360Degrees()
        => Assert.True(Math.Abs((1 << 24) * ByteOps.CoordUnit - 360.0) < 1e-9);

    [Fact]
    public void EmptyLabelsReturnEmptyString()
        => Assert.Equal("", new LabelSubfile(null).Get(5));
}

/// <summary>The TrailIntel ATV maps trigger two behaviours the decoder must get
/// right: distinct per-type trail colours (a simplestyle <c>stroke</c>) and POI
/// sprite <c>category</c>, plus rejection of mid-string label mis-reads.</summary>
public class GarminTypeClassTests
{
    [Theory]
    [InlineData("0x0d", "#E2571E")]   // curated trail colour
    [InlineData("0x0f", "#F0A81A")]
    public void KnownLineTypesGetCuratedStroke(string type, string color)
        => Assert.Equal(color, GarminTypeClasses.ClassifyLine(type).Color);

    [Fact]
    public void UnknownLineTypeStillGetsADistinctColor()
    {
        var color = GarminTypeClasses.ClassifyLine("0x7e").Color;
        Assert.StartsWith("#", color);              // never null → always renders apart
    }

    [Theory]
    [InlineData("0x2f01", "fuel")]     // confirmed via "CHEVRON GAS…"
    [InlineData("0x2a00", "food")]     // confirmed via "…/DQ", "…RESTAURANT"
    [InlineData("0x2b03", "lodging")]
    public void PoiTypesMapToKmlSpriteCategories(string type, string category)
        => Assert.Equal(category, GarminTypeClasses.CategoryForPoint(type));

    [Fact]
    public void UnconfirmedPoiTypeFallsBackToGenericSprite()
        => Assert.Null(GarminTypeClasses.CategoryForPoint("0x2c04"));
}

public class LabelBoundaryTests
{
    // 6-bit symbols: 0=space, 1-26 = A-Z, 32-41 = 0-9; >0x2F terminates.
    private static int Sym(char c) => c switch
    {
        ' ' => 0,
        >= 'A' and <= 'Z' => c - 'A' + 1,
        >= '0' and <= '9' => c - '0' + 32,
        _ => throw new ArgumentOutOfRangeException(nameof(c)),
    };

    /// <summary>Pack labels as 6-bit records (0x3F terminator) into an LBL subfile
    /// with mult=0, coding=6; returns the bytes and each label's <b>logical</b>
    /// offset (what <see cref="LabelSubfile.Get"/> takes). Logical offset 0 is
    /// reserved as "no label", so a dummy empty record is emitted first.</summary>
    private static (byte[] lbl, int[] starts) BuildLbl(params string[] labels)
    {
        const int dataOff = 0x20;
        var bits = new List<bool>();
        var starts = new List<int>();
        void Put6(int v) { for (int b = 5; b >= 0; b--) bits.Add((v & (1 << b)) != 0); }
        void Pad() { while (bits.Count % 8 != 0) bits.Add(false); }

        Put6(0x3F); Pad();                          // reserve logical offset 0 (empty)
        foreach (var label in labels)
        {
            starts.Add(bits.Count / 8);             // logical offset (mult=0 → byte index)
            foreach (var ch in label) Put6(Sym(ch));
            Put6(0x3F); Pad();                       // terminator, aligned to byte
        }

        var data = new byte[dataOff + bits.Count / 8];
        for (int i = 0; i < bits.Count; i++)
            if (bits[i]) data[dataOff + i / 8] |= (byte)(1 << (7 - i % 8));

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x15), dataOff);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x19), (uint)(bits.Count / 8));
        data[0x1D] = 0;   // mult
        data[0x1E] = 6;   // coding
        return (data, starts.ToArray());
    }

    [Fact]
    public void RealLabelBoundariesDecodeAndValidate()
    {
        var (lbl, starts) = BuildLbl("PARK", "TRAILHEAD");
        var labels = new LabelSubfile(lbl);

        Assert.Equal("PARK", labels.Get(starts[0]));
        Assert.Equal("TRAILHEAD", labels.Get(starts[1]));
        Assert.True(labels.IsRealLabel(starts[0]));
        Assert.True(labels.IsRealLabel(starts[1]));
    }

    [Fact]
    public void MidLabelOffsetIsRejectedAsFragment()
    {
        // "TRAILHEAD" spans several bytes; an offset one byte in lands mid-string.
        var (lbl, starts) = BuildLbl("PARK", "TRAILHEAD");
        var labels = new LabelSubfile(lbl);
        int mid = starts[1] + 1;

        Assert.False(labels.IsRealLabel(mid));          // not a real boundary
        Assert.NotEqual("TRAILHEAD", labels.Get(mid));  // raw decode would be a fragment
    }
}

public class GarminFormatDetectorTests
{
    [Fact]
    public void SniffsDskImgMagicAsGarmin()
    {
        var buf = new byte[0x20];
        "DSKIMG"u8.CopyTo(buf.AsSpan(0x10));
        using var stream = new MemoryStream(buf);
        Assert.Equal(GisFormat.GarminImg, FormatDetector.Sniff(stream));
    }
}

public class ImgParserEndToEndTests
{
    private const int Bits = 24;               // shift = 24 - bits = 0
    private const int CLng = 190_000, CLat = 95_000;
    private const int Dx = 10, Dy = -5;
    private const byte PointType = 0x2C;

    [Fact]
    public void DecodesSinglePointTile()
    {
        var img = BuildSinglePointImg();
        using var stream = new MemoryStream(img);
        var features = new ImgParser().Parse(stream);

        var f = Assert.Single(features);
        Assert.Equal("Point", f.GeometryType);
        Assert.Equal(GeometryFamily.Point, f.Family);

        var coords = JsonSerializer.Deserialize<double[]>(f.CoordinatesJson)!;
        Assert.Equal((CLng + Dx) * ByteOps.CoordUnit, coords[0], 6);
        Assert.Equal((CLat + Dy) * ByteOps.CoordUnit, coords[1], 6);

        using var props = JsonDocument.Parse(f.PropertiesJson);
        Assert.Equal("0x2c00", props.RootElement.GetProperty("garmin_type").GetString());
        Assert.Equal(0, props.RootElement.GetProperty("zoom").GetInt32());
    }

    /// <summary>Builds a 3-block .img: header+FAT in block 0, TRE in block 1,
    /// RGN in block 2, describing one tile "000000" with a single point.</summary>
    private static byte[] BuildSinglePointImg()
    {
        const int block = 8192;
        var buf = new byte[3 * block];

        // Header: DSKIMG signature at 0x10 (only needed by the sniffer) and the
        // block-size exponents at 0x61/0x62 giving 1 << (13 + 0) == 8192.
        "DSKIMG"u8.CopyTo(buf.AsSpan(0x10));
        buf[0x61] = 13;
        buf[0x62] = 0;

        // ---- TRE subfile in block 1 -------------------------------------
        int tre = block;
        PutS24(buf, tre + 0x15, 100_000);   // n
        PutS24(buf, tre + 0x18, 200_000);   // e
        PutS24(buf, tre + 0x1B, 90_000);    // s
        PutS24(buf, tre + 0x1E, 180_000);   // w
        PutU32(buf, tre + 0x21, 0x30);      // levels offset
        PutU32(buf, tre + 0x25, 0x04);      // levels size (one 4-byte level)
        PutU32(buf, tre + 0x29, 0x34);      // TRE2 (subdivisions) offset
        // level @0x30: zoom 0, bits 24, nsub 1
        buf[tre + 0x30] = 0x00;
        buf[tre + 0x31] = Bits;
        PutU16(buf, tre + 0x32, 1);
        // subdivision @0x34 (14 bytes, last level)
        int sd = tre + 0x34;
        PutU24(buf, sd + 0, 0);             // rgn_start
        buf[sd + 3] = TreSubfile.ElemPoint; // elements
        PutS24(buf, sd + 4, CLng);
        PutS24(buf, sd + 7, CLat);
        PutU16(buf, sd + 10, 0x100);        // w
        PutU16(buf, sd + 12, 0x100);        // h
        int treSize = 0x42;

        // ---- RGN subfile in block 2 -------------------------------------
        int rgn = 2 * block;
        const int rgnDataOff = 0x1D;
        PutU32(buf, rgn + 0x15, rgnDataOff);
        int po = rgn + rgnDataOff;
        buf[po] = PointType;                // point type
        PutU24(buf, po + 1, 0);             // pinfo: no label, no subtype
        PutS16(buf, po + 4, Dx);
        PutS16(buf, po + 6, Dy);
        const int rgnDataLen = 8;
        PutU32(buf, rgn + 0x19, rgnDataLen);
        int rgnSize = rgnDataOff + rgnDataLen;

        // ---- FAT directory @0x600: TRE then RGN entry -------------------
        WriteFatEntry(buf, 0x600, "000000", "TRE", treSize, blockNo: 1);
        WriteFatEntry(buf, 0x800, "000000", "RGN", rgnSize, blockNo: 2);
        return buf;
    }

    private static void WriteFatEntry(byte[] buf, int off, string name, string type, int size, int blockNo)
    {
        buf[off] = 0x01;                    // flag
        for (int i = 0; i < name.Length; i++) buf[off + 1 + i] = (byte)name[i];
        for (int i = 0; i < type.Length; i++) buf[off + 9 + i] = (byte)type[i];
        PutU32(buf, off + 0x0C, (uint)size);
        PutU16(buf, off + 0x10, 0);         // part
        PutU16(buf, off + 0x20, (ushort)blockNo);
        PutU16(buf, off + 0x22, 0xFFFF);    // block-list terminator
    }

    private static void PutU16(byte[] b, int o, int v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), (ushort)v);
    private static void PutU32(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);
    private static void PutS16(byte[] b, int o, int v) => BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(o), (short)v);
    private static void PutU24(byte[] b, int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); }
    private static void PutS24(byte[] b, int o, int v) => PutU24(b, o, v & 0xFFFFFF);
}
