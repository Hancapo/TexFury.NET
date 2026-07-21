using System.Buffers.Binary;

namespace TexFury;

/// <summary>RAGE resource format (RSC5) for GTA IV .wtd files.</summary>
internal static class Rsc5
{
    public const uint Rsc5Magic = 0x05435352;
    private const uint ResourceTypeTexture = 0x8;

    private static (int Count, int Shift) GetCountShift(int size)
    {
        if (size <= 0) return (0, 0);
        int shift = 0;
        while (true)
        {
            int blockSize = 1 << (shift + 8);
            int count = (size + blockSize - 1) / blockSize;
            if (count <= 0x7FF)
                return (count, shift);
            shift++;
            if (shift > 0xF)
                return (0x7FF, shift);
        }
    }

    public static uint BuildRsc5Flags(int virtualSize, int physicalSize, int version = 3)
    {
        var (vc, vs) = GetCountShift(virtualSize);
        var (pc, ps) = GetCountShift(physicalSize);
        uint flags = (uint)(version & 0x3) << 30;
        flags |= (uint)(vc & 0x7FF);
        flags |= (uint)(vs & 0xF) << 11;
        flags |= (uint)(pc & 0x7FF) << 15;
        flags |= (uint)(ps & 0xF) << 26;
        return flags;
    }

    public static (int Version, int VirtualSize, int PhysicalSize) DecodeRsc5Flags(uint flags)
    {
        int version = (int)((flags >> 30) & 0x3);
        int vc = (int)(flags & 0x7FF);
        int vs = (int)((flags >> 11) & 0xF);
        int pc = (int)((flags >> 15) & 0x7FF);
        int ps = (int)((flags >> 26) & 0xF);
        return (version, vc << (vs + 8), pc << (ps + 8));
    }

    public static byte[] BuildRsc5(byte[] virtualData, byte[] physicalData, int version = 3)
    {
        uint flags = BuildRsc5Flags(virtualData.Length, physicalData.Length, version);
        var (_, vAlloc, pAlloc) = DecodeRsc5Flags(flags);

        byte[] padded = new byte[vAlloc + pAlloc];
        Array.Copy(virtualData, padded, virtualData.Length);
        Array.Copy(physicalData, 0, padded, vAlloc, physicalData.Length);

        byte[] compressed = Resource.ZlibCompress(padded);
        byte[] result = new byte[12 + compressed.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result, Rsc5Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), ResourceTypeTexture);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), flags);
        Array.Copy(compressed, 0, result, 12, compressed.Length);
        return result;
    }

    public static (byte[] virtualData, byte[] physicalData) DecompressRsc5(byte[] data)
    {
        if (data.Length < 12)
            throw new InvalidDataException("Data too short for RSC5 header");

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (magic != Rsc5Magic)
            throw new InvalidDataException($"Bad RSC5 magic: 0x{magic:X8}");

        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        var (_, vSize, pSize) = DecodeRsc5Flags(flags);
        byte[] raw = Resource.ZlibDecompress(data.AsSpan(12).ToArray(), vSize + pSize);

        byte[] virt = new byte[vSize];
        byte[] phys = new byte[pSize];
        Array.Copy(raw, virt, vSize);
        Array.Copy(raw, vSize, phys, 0, pSize);
        return (virt, phys);
    }
}
