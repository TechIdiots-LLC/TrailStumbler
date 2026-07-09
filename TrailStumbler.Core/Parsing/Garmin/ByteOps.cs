using System.Buffers.Binary;

namespace TrailStumbler.Core.Parsing.Garmin;

/// <summary>Little-endian byte-reading helpers (Garmin uses little-endian
/// throughout). Ported from GarminBridge's <c>_struct.py</c>.</summary>
internal static class ByteOps
{
    /// <summary>Degrees per Garmin 24-bit coordinate unit.</summary>
    public const double CoordUnit = 360.0 / (1 << 24);

    public static ushort U16(byte[] d, int o) => BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o, 2));
    public static uint U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o, 4));
    public static short S16(byte[] d, int o) => BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(o, 2));

    public static int U24(byte[] d, int o) => d[o] | (d[o + 1] << 8) | (d[o + 2] << 16);

    public static int S24(byte[] d, int o)
    {
        int v = U24(d, o);
        return (v & 0x800000) != 0 ? v - 0x1000000 : v;
    }
}
