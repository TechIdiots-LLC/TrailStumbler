namespace TrailStumbler.Core.Parsing.Garmin;

/// <summary>LSB-first bit reader used by the RGN polyline/polygon coordinate
/// bitstream. Reads bits from a window <c>[start, start+length)</c> of a larger buffer so no slice copy is needed per
/// element, and never past it.</summary>
internal sealed class BitReader
{
    private readonly byte[] _d;
    private readonly int _start;
    private readonly int _bits;
    private int _pos;   // bit position relative to _start

    public BitReader(byte[] data, int start, int length)
    {
        _d = data;
        _start = start;
        _bits = Math.Max(0, Math.Min(length, data.Length - start)) * 8;
    }

    /// <summary>Bits left in the window.</summary>
    public int Remaining => _bits - _pos;

    /// <summary>Read <paramref name="n"/> bits, least-significant-bit first, as an unsigned int.</summary>
    public int Get(int n)
    {
        if (n > Remaining) throw new EndOfStreamException("past the end of the bitstream");
        int v = 0;
        int p = _pos;
        for (int k = 0; k < n; k++)
        {
            v |= ((_d[_start + (p >> 3)] >> (p & 7)) & 1) << k;
            p++;
        }
        _pos = p;
        return v;
    }

    /// <summary>
    /// A signed <paramref name="n"/>-bit value as Garmin writes a coordinate delta whose sign varies point to point
    /// (mkgmap's BitWriter.sputn / BitReader.sget2): the top bit is the sign, and a value bigger than fits is written
    /// as repeats of the bare sign bit, each adding top - 1 to it, before the final value.
    /// </summary>
    public int SGet2(int n)
    {
        int top = 1 << (n - 1);
        int mask = top - 1;
        int @base = 0;
        int res = Get(n);
        while (res == top)
        {
            @base += mask;
            res = Get(n);
        }
        return (res & top) == 0 ? res + @base : (res | ~mask) - @base;
    }
}
