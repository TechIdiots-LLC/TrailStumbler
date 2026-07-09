namespace TrailStumbler.Core.Parsing.Garmin;

/// <summary>LSB-first bit reader used by the RGN polyline/polygon coordinate
/// bitstream. Ported from GarminBridge's <c>bitreader.py</c>. Reads bits from a
/// window <c>[start, start+length)</c> of a larger buffer so no slice copy is
/// needed per element.</summary>
internal sealed class BitReader
{
    private readonly byte[] _d;
    private readonly int _start;
    private int _pos;   // bit position relative to _start

    public BitReader(byte[] data, int start, int length)
    {
        _d = data;
        _start = start;
        _ = length; // window length is implied by callers; retained for parity
    }

    /// <summary>Read <paramref name="n"/> bits, least-significant-bit first, as an unsigned int.</summary>
    public int Get(int n)
    {
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
}
