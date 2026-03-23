using System.Buffers.Binary;

namespace TexFury;

/// <summary>Builds DDS file bytes from compressed pixel data.</summary>
internal static class DdsBuilder
{
    private const uint DDS_MAGIC = 0x20534444;
    private const uint DDSD_CAPS = 0x1;
    private const uint DDSD_HEIGHT = 0x2;
    private const uint DDSD_WIDTH = 0x4;
    private const uint DDSD_PITCH = 0x8;
    private const uint DDSD_PIXELFORMAT = 0x1000;
    private const uint DDSD_MIPMAPCOUNT = 0x20000;
    private const uint DDSD_LINEARSIZE = 0x80000;
    private const uint DDPF_ALPHAPIXELS = 0x1;
    private const uint DDPF_FOURCC = 0x4;
    private const uint DDPF_RGB = 0x40;
    private const uint DDSCAPS_TEXTURE = 0x1000;
    private const uint DDSCAPS_COMPLEX = 0x8;
    private const uint DDSCAPS_MIPMAP = 0x400000;

    public static byte[] Build(int width, int height, BCFormat fmt,
                               int mipCount, int[] mipSizes, byte[] pixelData)
    {
        bool uncompressed = !Formats.IsBlockCompressed(fmt);
        bool useDx10 = !uncompressed && fmt is BCFormat.BC4 or BCFormat.BC5 or BCFormat.BC7;

        int headerSize = 4 + 124 + (useDx10 ? 20 : 0);
        byte[] result = new byte[headerSize + pixelData.Length];
        Span<byte> buf = result.AsSpan();

        // Magic
        BinaryPrimitives.WriteUInt32LittleEndian(buf, DDS_MAGIC);

        // DDS_HEADER (124 bytes at offset 4)
        Span<byte> hdr = buf[4..];
        BinaryPrimitives.WriteUInt32LittleEndian(hdr, 124); // size

        uint flags = DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT;
        flags |= uncompressed ? DDSD_PITCH : DDSD_LINEARSIZE;
        if (mipCount > 1) flags |= DDSD_MIPMAPCOUNT;
        BinaryPrimitives.WriteUInt32LittleEndian(hdr[4..], flags);

        BinaryPrimitives.WriteUInt32LittleEndian(hdr[8..], (uint)height);
        BinaryPrimitives.WriteUInt32LittleEndian(hdr[12..], (uint)width);

        if (uncompressed)
            BinaryPrimitives.WriteUInt32LittleEndian(hdr[16..], (uint)(width * 4)); // pitch
        else
            BinaryPrimitives.WriteUInt32LittleEndian(hdr[16..], mipSizes.Length > 0 ? (uint)mipSizes[0] : 0);

        BinaryPrimitives.WriteUInt32LittleEndian(hdr[20..], 1); // depth
        BinaryPrimitives.WriteUInt32LittleEndian(hdr[24..], (uint)mipCount);
        // reserved1[11] stays zero

        // Pixel format at header offset 72
        BinaryPrimitives.WriteUInt32LittleEndian(hdr[72..], 32); // pf.size

        if (uncompressed)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(hdr[76..], DDPF_RGB | DDPF_ALPHAPIXELS);
            BinaryPrimitives.WriteUInt32LittleEndian(hdr[84..], 32);          // rgbBitCount
            BinaryPrimitives.WriteUInt32LittleEndian(hdr[88..], 0x00FF0000);  // rBitMask
            BinaryPrimitives.WriteUInt32LittleEndian(hdr[92..], 0x0000FF00);  // gBitMask
            BinaryPrimitives.WriteUInt32LittleEndian(hdr[96..], 0x000000FF);  // bBitMask
            BinaryPrimitives.WriteUInt32LittleEndian(hdr[100..], 0xFF000000); // aBitMask
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(hdr[76..], DDPF_FOURCC);
            if (useDx10)
                BinaryPrimitives.WriteUInt32LittleEndian(hdr[80..], Formats.FourCC_DX10);
            else if (fmt == BCFormat.BC1)
                BinaryPrimitives.WriteUInt32LittleEndian(hdr[80..], Formats.FourCC_DXT1);
            else
                BinaryPrimitives.WriteUInt32LittleEndian(hdr[80..], Formats.FourCC_DXT5);
        }

        uint caps = DDSCAPS_TEXTURE;
        if (mipCount > 1) caps |= DDSCAPS_COMPLEX | DDSCAPS_MIPMAP;
        BinaryPrimitives.WriteUInt32LittleEndian(hdr[104..], caps);

        int offset = 4 + 124;

        // DX10 extended header
        if (useDx10)
        {
            Span<byte> dx10 = buf[offset..];
            BinaryPrimitives.WriteUInt32LittleEndian(dx10, (uint)Formats.ToDxgi(fmt));
            BinaryPrimitives.WriteUInt32LittleEndian(dx10[4..], 3); // TEXTURE2D
            BinaryPrimitives.WriteUInt32LittleEndian(dx10[12..], 1); // arraySize
            offset += 20;
        }

        pixelData.CopyTo(result, offset);
        return result;
    }
}
