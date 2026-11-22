namespace TrailStumbler.Core.Parsing.Garmin;

/// <summary>
/// NET subfile: a routable map's road records. A routable line's name is kept here rather than in its own label
/// (its RGN label has bit 23 set and addresses a road record), as its first label: 3 bytes, the LBL offset in the
/// low 22 bits (mkgmap's RoadDef).
/// </summary>
internal sealed class NetSubfile
{
    private readonly byte[] _data;
    private readonly int _roadsOff;
    private readonly int _roadsLen;
    private readonly int _mult;

    public NetSubfile(byte[]? data)
    {
        _data = data ?? [];
        if (_data.Length < 0x1E) return;
        _roadsOff = (int)ByteOps.U32(_data, 0x15);
        _roadsLen = (int)ByteOps.U32(_data, 0x19);
        _mult = _data[0x1D];
    }

    /// <summary>The LBL offset of the road record's first label, or 0 when there's none.</summary>
    public int RoadLabel(int roadOffset)
    {
        long p = _roadsOff + ((long)roadOffset << _mult);
        if (_roadsLen == 0 || p < _roadsOff || p + 3 > Math.Min(_data.Length, (long)_roadsOff + _roadsLen)) return 0;
        return ByteOps.U24(_data, (int)p) & 0x3FFFFF;
    }
}
