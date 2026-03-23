using System.Buffers.Binary;
using System.IO.Compression;

namespace TexFury;

/// <summary>RAGE resource format (RSC7) encoding and assembly.</summary>
internal static class Resource
{
    public const long VirtualBase = 0x50000000;
    public const long PhysicalBase = 0x60000000;
    public const int BaseSize = 0x2000;
    public const uint Rsc7Magic = 0x37435352;
    public const int Rsc7VersionYtd = 13;

    public static List<int> DecodeChunkSizes(uint data)
    {
        if (data == 0) return [];

        int shift = (int)(data & 0xF) + 4;
        int baseChunk = BaseSize << shift;

        int[] counts =
        [
            (int)((data >> 4) & 0x1),
            (int)((data >> 5) & 0x3),
            (int)((data >> 7) & 0xF),
            (int)((data >> 11) & 0x3F),
            (int)((data >> 17) & 0x7F),
            (int)((data >> 24) & 0x1),
            (int)((data >> 25) & 0x1),
            (int)((data >> 26) & 0x1),
            (int)((data >> 27) & 0x1),
        ];

        var sizes = new List<int>();
        int currentSize = baseChunk;
        foreach (int c in counts)
        {
            for (int i = 0; i < c; i++)
                sizes.Add(currentSize);
            currentSize >>= 1;
        }
        return sizes;
    }

    public static int TotalFromFlags(uint data) => DecodeChunkSizes(data).Sum();

    public static uint EncodeFlags(int neededSize)
    {
        if (neededSize <= 0) return 0;
        for (int shift = 0; shift < 16; shift++)
        {
            int chunkSize = BaseSize << (shift + 4);
            if (chunkSize >= neededSize)
                return (uint)shift | (1u << 4);
        }
        throw new ArgumentException($"Size {neededSize} exceeds maximum single chunk");
    }

    public static byte[] DeflateCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
            ds.Write(data);
        return ms.ToArray();
    }

    public static byte[] DeflateDecompress(byte[] data, int expectedSize)
    {
        using var input = new MemoryStream(data);
        using var ds = new DeflateStream(input, CompressionMode.Decompress);
        byte[] result = new byte[expectedSize];
        int read = 0;
        while (read < expectedSize)
        {
            int n = ds.Read(result, read, expectedSize - read);
            if (n == 0) break;
            read += n;
        }
        return result;
    }

    public static byte[] BuildRsc7(byte[] virtualData, byte[] physicalData,
                                    int version = Rsc7VersionYtd)
    {
        uint sysFlags = EncodeFlags(virtualData.Length);
        uint gfxFlags = physicalData.Length > 0 ? EncodeFlags(physicalData.Length) : 0;

        int sysChunkSize = TotalFromFlags(sysFlags);
        int gfxChunkSize = gfxFlags != 0 ? TotalFromFlags(gfxFlags) : 0;

        byte[] padded = new byte[sysChunkSize + gfxChunkSize];
        Array.Copy(virtualData, 0, padded, 0, virtualData.Length);
        if (physicalData.Length > 0)
            Array.Copy(physicalData, 0, padded, sysChunkSize, physicalData.Length);

        byte[] compressed = DeflateCompress(padded);

        byte[] header = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(header, Rsc7Magic);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), version);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), sysFlags);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), gfxFlags);

        byte[] result = new byte[16 + compressed.Length];
        Array.Copy(header, result, 16);
        Array.Copy(compressed, 0, result, 16, compressed.Length);
        return result;
    }

    public static (uint version, uint sysFlags, uint gfxFlags) ParseRsc7Header(byte[] data)
    {
        if (data.Length < 16)
            throw new InvalidDataException("Data too short for RSC7 header");
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (magic != Rsc7Magic)
            throw new InvalidDataException($"Bad RSC7 magic: 0x{magic:X8}");
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        uint sysFlags = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        uint gfxFlags = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        return (version, sysFlags, gfxFlags);
    }

    public static (byte[] virtualData, byte[] physicalData) DecompressRsc7(byte[] data)
    {
        var (_, sysFlags, gfxFlags) = ParseRsc7Header(data);
        int sysSize = TotalFromFlags(sysFlags);
        int gfxSize = TotalFromFlags(gfxFlags);

        byte[] raw = DeflateDecompress(data.AsSpan(16).ToArray(), sysSize + gfxSize);

        byte[] virt = new byte[sysSize];
        byte[] phys = new byte[gfxSize];
        Array.Copy(raw, 0, virt, 0, sysSize);
        Array.Copy(raw, sysSize, phys, 0, gfxSize);
        return (virt, phys);
    }
}
