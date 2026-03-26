using System.Buffers.Binary;
using System.IO.Compression;

namespace TexFury;

/// <summary>RAGE resource format (RSC8) encoding and assembly for RDR2.</summary>
internal static class Rsc8
{
    public const uint Rsc8Magic = 0x38435352; // "RSC8" LE
    public const int Rsc8VersionYtd = 2;

    private static int Align(int size, int alignment) =>
        (size + alignment - 1) & ~(alignment - 1);

    public static byte[] BuildRsc8(byte[] virtualData, byte[] physicalData,
                                    int version = Rsc8VersionYtd)
    {
        int vSize = virtualData.Length;
        int pSize = physicalData.Length;

        int vAligned = Align(vSize, vSize > 0x8000 ? 0x10000 : 16);
        int pAligned = Align(pSize, pSize > 0x8000 ? 0x10000 : 16);

        uint vFlags = (uint)(vAligned & 0xFFFFFFF0) | (uint)((version >> 4) & 0xF);
        uint pFlags = (uint)(pAligned & 0xFFFFFFF0) | (uint)(version & 0xF);

        byte[] padded = new byte[vAligned + pAligned];
        Array.Copy(virtualData, 0, padded, 0, vSize);
        Array.Copy(physicalData, 0, padded, vAligned, pSize);

        byte[] compressed = Resource.DeflateCompress(padded);

        byte[] header = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(header, Rsc8Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)(version & 0xFF));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), vFlags);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), pFlags);

        byte[] result = new byte[16 + compressed.Length];
        Array.Copy(header, result, 16);
        Array.Copy(compressed, 0, result, 16, compressed.Length);
        return result;
    }

    public static (byte[] virtualData, byte[] physicalData) DecompressRsc8(byte[] data)
    {
        if (data.Length < 16)
            throw new InvalidDataException("Data too short for RSC8 header");

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (magic != Rsc8Magic)
            throw new InvalidDataException($"Bad RSC8 magic: 0x{magic:X8}");

        uint vFlags = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        uint pFlags = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));

        int vSize = (int)(vFlags & 0xFFFFFFF0);
        int pSize = (int)(pFlags & 0xFFFFFFF0);

        byte[] raw = Resource.DeflateDecompress(data.AsSpan(16).ToArray(), vSize + pSize);

        byte[] virt = new byte[vSize];
        byte[] phys = new byte[pSize];
        Array.Copy(raw, 0, virt, 0, vSize);
        Array.Copy(raw, vSize, phys, 0, pSize);
        return (virt, phys);
    }
}
