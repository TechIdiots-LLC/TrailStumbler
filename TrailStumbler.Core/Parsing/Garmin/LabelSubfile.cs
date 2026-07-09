using System.Text;

namespace TrailStumbler.Core.Parsing.Garmin;

/// <summary>
/// LBL subfile: resolve label offsets to text. Ported from GarminBridge's
/// <c>labels.py</c>.
///
/// Garmin 6-bit encoding (uppercase + digits, the common case for these maps).
/// The character table and the "&gt;0x2F terminates" rule are from the imgdecode
/// reference. 8-bit coding is handled as a raw fallback.
/// </summary>
internal sealed class LabelSubfile
{
    private const string Enc6 = " ABCDEFGHIJKLMNOPQRSTUVWXYZ~~~~~0123456789~~~~~~";
    private const string Enc6Shift = "@!\"#$%&'()*+,-./~~~~~~~~~~:;<=>?~~~~~~~~~~~[\\]^_";
    private const string Enc6Spec = "`abcdefghijklmnopqrstuvwxyz~~~~~0123456789~~~~~~";

    private readonly bool _ok;
    private readonly byte[] _data = Array.Empty<byte>();
    private readonly int _dataOff;
    private readonly int _dataLen;
    private readonly int _mult;
    private readonly int _coding;

    /// <summary>Logical offsets at which a real, packed label record begins. A
    /// point/line whose label offset is not one of these is pointing into the
    /// middle of some other label — a mis-read that yields a truncated fragment
    /// ("BEMIS MTN OVERLOOK" → "N OVERLOOK" → "OK"). Used by
    /// <see cref="IsRealLabel"/> to reject those. Built lazily on first use.</summary>
    private HashSet<int>? _validStarts;

    public LabelSubfile(byte[]? data)
    {
        _ok = data is { Length: > 0 };
        if (!_ok) return;
        _data = data!;
        _dataOff = (int)ByteOps.U32(_data, 0x15);
        _dataLen = (int)ByteOps.U32(_data, 0x19);
        _mult = _data[0x1D];    // label address multiplier
        _coding = _data[0x1E];  // 6 = 6-bit, else treated as 8-bit
    }

    public string Get(int offset)
    {
        if (!_ok || offset == 0) return "";
        int basep = _dataOff + (offset << _mult);
        return _coding == 6 ? Decode6(basep) : Decode8(basep);
    }

    /// <summary>True when <paramref name="offset"/> addresses the start of an
    /// actual label record (not a mid-label fragment). Only meaningful for 6-bit
    /// coding; 8-bit maps accept any non-zero offset.</summary>
    public bool IsRealLabel(int offset)
    {
        if (!_ok || offset == 0) return false;
        if (_coding != 6) return true;
        return (_validStarts ??= BuildValidStarts()).Contains(offset);
    }

    /// <summary>Walk the packed label section start-to-end, recording the aligned
    /// logical offset at which each label begins.</summary>
    private HashSet<int> BuildValidStarts()
    {
        var starts = new HashSet<int>();
        int align = 1 << _mult;
        int end = Math.Min(_dataOff + _dataLen, _data.Length);
        int p = _dataOff;
        while (p < end)
        {
            starts.Add((p - _dataOff) >> _mult);
            int next = Decode6End(p, end);
            if (next <= p) next = p + 1;
            // Labels are padded to the address-multiplier boundary.
            int rel = ((next - _dataOff + align - 1) / align) * align;
            p = _dataOff + rel;
        }
        return starts;
    }

    /// <summary>Position just past a label's terminator, starting at byte <paramref name="p"/>.</summary>
    private int Decode6End(int p, int end)
    {
        int acc = 0, nb = 0;
        while (p < end)
        {
            acc = (acc << 8) | _data[p]; nb += 8; p++;
            while (nb >= 6)
            {
                nb -= 6;
                if (((acc >> nb) & 0x3F) > 0x2F) return p;   // terminator
            }
        }
        return p;
    }

    private string Decode8(int p)
    {
        var d = _data;
        var sb = new StringBuilder();
        int end = _dataOff + _dataLen;
        while (p < end && p < d.Length && d[p] != 0)
        {
            sb.Append((char)d[p]);
            p++;
            if (sb.Length > 80) break;
        }
        return sb.ToString();
    }

    private string Decode6(int p)
    {
        var d = _data;
        int end = Math.Min(_dataOff + _dataLen, d.Length);
        var sb = new StringBuilder();
        int acc = 0, nb = 0;
        string table = Enc6;
        while (p < end)
        {
            acc = (acc << 8) | d[p];
            nb += 8;
            p++;
            while (nb >= 6)
            {
                nb -= 6;
                int sym = (acc >> nb) & 0x3F;
                if (sym > 0x2F)                     // terminator
                    return sb.ToString().Trim();
                if (sym == 0x1B) { table = Enc6Spec; continue; }   // shift: special table
                if (sym == 0x1C) { table = Enc6Shift; continue; }  // shift: symbol table
                char c = table[sym];
                table = Enc6;
                if (c != '~') sb.Append(c);
                if (sb.Length > 80) return sb.ToString().Trim();
            }
        }
        return sb.ToString().Trim();
    }
}
