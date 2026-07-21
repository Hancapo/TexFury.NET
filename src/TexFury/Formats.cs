namespace TexFury;

/// <summary>DXGI_FORMAT values used in DDS DX10 headers.</summary>
internal enum DxgiFormat : uint
{
    R32G32B32A32Float = 2,
    R16G16B16A16Float = 10,
    R10G10B10A2Unorm = 24,
    R8G8B8A8Unorm = 28,
    R16G16Float = 34,
    R32Float = 41,
    R8G8Unorm = 49,
    R16Float = 54,
    R8Unorm = 61,
    A8Unorm = 65,
    BC1Unorm = 71,
    BC2Unorm = 74,
    BC3Unorm = 77,
    BC4Unorm = 80,
    BC5Unorm = 83,
    B5G6R5Unorm = 85,
    B5G5R5A1Unorm = 86,
    B8G8R8A8Unorm = 87,
    BC6HUf16 = 95,
    BC7Unorm = 98,
}

/// <summary>RSC8/Enhanced texture format byte values.</summary>
public enum Rsc8TextureFormat : byte
{
    BC1Unorm = 0x47,
    BC1UnormSrgb = 0x48,
    BC2Unorm = 0x4A,
    BC2UnormSrgb = 0x4B,
    BC3Unorm = 0x4D,
    BC3UnormSrgb = 0x4E,
    BC4Unorm = 0x50,
    BC5Unorm = 0x53,
    BC6HUf16 = 0x5F,
    BC7Unorm = 0x62,
    BC7UnormSrgb = 0x63,
    R8Unorm = 0x3D,
    A8Unorm = 0x41,
    R8G8Unorm = 0x31,
    R8G8B8A8Unorm = 0x1C,
    R8G8B8A8UnormSrgb = 0x1D,
    B8G8R8A8Unorm = 0x57,
    B8G8R8A8UnormSrgb = 0x5B,
    B5G6R5Unorm = 0x55,
    B5G5R5A1Unorm = 0x56,
    R10G10B10A2Unorm = 0x18,
    R16Float = 0x36,
    R16G16Float = 0x22,
    R16G16B16A16Float = 0x0A,
    R32Float = 0x29,
    R32G32B32A32Float = 0x02,
}

internal enum Rsc5TextureFormat : uint
{
    DXT1 = Formats.FourCC_DXT1,
    DXT3 = Formats.FourCC_DXT3,
    DXT5 = Formats.FourCC_DXT5,
    A8R8G8B8 = Formats.D3DFMT_A8R8G8B8,
    A1R5G5B5 = Formats.D3DFMT_A1R5G5B5,
    R5G6B5 = Formats.D3DFMT_R5G6B5,
    A8 = Formats.D3DFMT_A8,
    L8 = Formats.D3DFMT_L8,
}

/// <summary>Format constants and utility methods.</summary>
internal static class Formats
{
    public const uint FourCC_DXT1 = 0x31545844;
    public const uint FourCC_DXT3 = 0x33545844;
    public const uint FourCC_DXT5 = 0x35545844;
    public const uint FourCC_ATI1 = 0x31495441;
    public const uint FourCC_ATI2 = 0x32495441;
    public const uint FourCC_BC7 = 0x20374342;
    public const uint FourCC_DX10 = 0x30315844;

    public const uint D3DFMT_A8R8G8B8 = 21;
    public const uint D3DFMT_R5G6B5 = 23;
    public const uint D3DFMT_A1R5G5B5 = 25;
    public const uint D3DFMT_A8 = 28;
    public const uint D3DFMT_L8 = 50;

    private static readonly HashSet<BCFormat> BlockCompressed =
    [
        BCFormat.BC1, BCFormat.BC1A, BCFormat.BC2, BCFormat.BC3, BCFormat.BC4,
        BCFormat.BC5, BCFormat.BC6H, BCFormat.BC7,
    ];

    private static readonly HashSet<BCFormat> Gta4Unsupported =
    [
        BCFormat.BC4, BCFormat.BC5, BCFormat.BC6H, BCFormat.BC7,
        BCFormat.R8G8B8A8, BCFormat.R10G10B10A2, BCFormat.R8G8,
        BCFormat.R16_FLOAT, BCFormat.R16G16_FLOAT, BCFormat.R16G16B16A16_FLOAT,
        BCFormat.R32_FLOAT, BCFormat.R32G32B32A32_FLOAT,
    ];

    public static bool IsBlockCompressed(BCFormat fmt) => BlockCompressed.Contains(fmt);

    public static bool IsGta4Supported(BCFormat fmt) => !Gta4Unsupported.Contains(fmt);

    public static int BlockByteSize(BCFormat fmt) => fmt switch
    {
        BCFormat.BC1 or BCFormat.BC1A or BCFormat.BC4 => 8,
        BCFormat.BC2 or BCFormat.BC3 or BCFormat.BC5 or BCFormat.BC6H or BCFormat.BC7 => 16,
        _ => throw new ArgumentException($"{fmt} is not block-compressed", nameof(fmt)),
    };

    public static int PixelByteSize(BCFormat fmt) => fmt switch
    {
        BCFormat.A8R8G8B8 or BCFormat.R8G8B8A8 or BCFormat.R10G10B10A2 => 4,
        BCFormat.B5G6R5 or BCFormat.B5G5R5A1 or BCFormat.R8G8 or BCFormat.R16_FLOAT => 2,
        BCFormat.R8 or BCFormat.A8 => 1,
        BCFormat.R16G16_FLOAT or BCFormat.R32_FLOAT => 4,
        BCFormat.R16G16B16A16_FLOAT => 8,
        BCFormat.R32G32B32A32_FLOAT => 16,
        _ => throw new ArgumentException($"{fmt} is block-compressed", nameof(fmt)),
    };

    public static int MipDataSize(int width, int height, BCFormat fmt)
    {
        if (IsBlockCompressed(fmt))
        {
            int bw = Math.Max(1, (width + 3) / 4);
            int bh = Math.Max(1, (height + 3) / 4);
            return bw * bh * BlockByteSize(fmt);
        }
        return width * height * PixelByteSize(fmt);
    }

    public static int TotalMipDataSize(int width, int height, BCFormat fmt, int levels)
    {
        int total = 0;
        int w = width, h = height;
        for (int i = 0; i < levels; i++)
        {
            total += MipDataSize(w, h, fmt);
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        return total;
    }

    public static int RowPitch(int width, BCFormat fmt)
    {
        if (!IsBlockCompressed(fmt))
            return width * PixelByteSize(fmt);
        int bw = Math.Max(1, (width + 3) / 4);
        return bw * BlockByteSize(fmt);
    }

    public static DxgiFormat ToDxgi(BCFormat fmt) => fmt switch
    {
        BCFormat.BC1 => DxgiFormat.BC1Unorm,
        BCFormat.BC1A => DxgiFormat.BC1Unorm,
        BCFormat.BC2 => DxgiFormat.BC2Unorm,
        BCFormat.BC3 => DxgiFormat.BC3Unorm,
        BCFormat.BC4 => DxgiFormat.BC4Unorm,
        BCFormat.BC5 => DxgiFormat.BC5Unorm,
        BCFormat.BC6H => DxgiFormat.BC6HUf16,
        BCFormat.BC7 => DxgiFormat.BC7Unorm,
        BCFormat.A8R8G8B8 => DxgiFormat.B8G8R8A8Unorm,
        BCFormat.R8G8B8A8 => DxgiFormat.R8G8B8A8Unorm,
        BCFormat.B5G6R5 => DxgiFormat.B5G6R5Unorm,
        BCFormat.B5G5R5A1 => DxgiFormat.B5G5R5A1Unorm,
        BCFormat.R10G10B10A2 => DxgiFormat.R10G10B10A2Unorm,
        BCFormat.R8 => DxgiFormat.R8Unorm,
        BCFormat.A8 => DxgiFormat.A8Unorm,
        BCFormat.R8G8 => DxgiFormat.R8G8Unorm,
        BCFormat.R16_FLOAT => DxgiFormat.R16Float,
        BCFormat.R16G16_FLOAT => DxgiFormat.R16G16Float,
        BCFormat.R16G16B16A16_FLOAT => DxgiFormat.R16G16B16A16Float,
        BCFormat.R32_FLOAT => DxgiFormat.R32Float,
        BCFormat.R32G32B32A32_FLOAT => DxgiFormat.R32G32B32A32Float,
        _ => throw new ArgumentOutOfRangeException(nameof(fmt)),
    };

    public static BCFormat FromDxgi(uint code) => code switch
    {
        (uint)DxgiFormat.BC1Unorm => BCFormat.BC1,
        (uint)DxgiFormat.BC2Unorm => BCFormat.BC2,
        (uint)DxgiFormat.BC3Unorm => BCFormat.BC3,
        (uint)DxgiFormat.BC4Unorm => BCFormat.BC4,
        (uint)DxgiFormat.BC5Unorm => BCFormat.BC5,
        (uint)DxgiFormat.BC6HUf16 => BCFormat.BC6H,
        (uint)DxgiFormat.BC7Unorm => BCFormat.BC7,
        (uint)DxgiFormat.B8G8R8A8Unorm => BCFormat.A8R8G8B8,
        (uint)DxgiFormat.R8G8B8A8Unorm => BCFormat.R8G8B8A8,
        (uint)DxgiFormat.B5G6R5Unorm => BCFormat.B5G6R5,
        (uint)DxgiFormat.B5G5R5A1Unorm => BCFormat.B5G5R5A1,
        (uint)DxgiFormat.R10G10B10A2Unorm => BCFormat.R10G10B10A2,
        (uint)DxgiFormat.R8Unorm => BCFormat.R8,
        (uint)DxgiFormat.A8Unorm => BCFormat.A8,
        (uint)DxgiFormat.R8G8Unorm => BCFormat.R8G8,
        (uint)DxgiFormat.R16Float => BCFormat.R16_FLOAT,
        (uint)DxgiFormat.R16G16Float => BCFormat.R16G16_FLOAT,
        (uint)DxgiFormat.R16G16B16A16Float => BCFormat.R16G16B16A16_FLOAT,
        (uint)DxgiFormat.R32Float => BCFormat.R32_FLOAT,
        (uint)DxgiFormat.R32G32B32A32Float => BCFormat.R32G32B32A32_FLOAT,
        _ => throw new ArgumentException($"Unsupported DXGI format: {code}"),
    };

    public static uint ToDx9(BCFormat fmt) => fmt switch
    {
        BCFormat.BC1 => FourCC_DXT1,
        BCFormat.BC1A => FourCC_DXT1,
        BCFormat.BC2 => FourCC_DXT3,
        BCFormat.BC3 => FourCC_DXT5,
        BCFormat.BC4 => FourCC_ATI1,
        BCFormat.BC5 => FourCC_ATI2,
        BCFormat.BC7 => FourCC_BC7,
        BCFormat.A8R8G8B8 => D3DFMT_A8R8G8B8,
        BCFormat.A8 => D3DFMT_A8,
        BCFormat.B5G5R5A1 => D3DFMT_A1R5G5B5,
        BCFormat.B5G6R5 => D3DFMT_R5G6B5,
        BCFormat.R8 => D3DFMT_L8,
        _ => throw new ArgumentOutOfRangeException(nameof(fmt), $"{fmt} has no DX9 mapping"),
    };

    public static BCFormat FromDx9(uint code) => code switch
    {
        FourCC_DXT1 => BCFormat.BC1,
        FourCC_DXT3 => BCFormat.BC2,
        FourCC_DXT5 => BCFormat.BC3,
        FourCC_ATI1 => BCFormat.BC4,
        FourCC_ATI2 => BCFormat.BC5,
        FourCC_BC7 => BCFormat.BC7,
        D3DFMT_A8R8G8B8 => BCFormat.A8R8G8B8,
        D3DFMT_A8 => BCFormat.A8,
        D3DFMT_A1R5G5B5 => BCFormat.B5G5R5A1,
        D3DFMT_R5G6B5 => BCFormat.B5G6R5,
        D3DFMT_L8 => BCFormat.R8,
        _ => throw new ArgumentException($"Unsupported DX9 format code: 0x{code:X8}"),
    };

    public static BCFormat SuggestFormat(bool hasAlpha,
        bool normalMap = false, bool singleChannel = false,
        bool qualityOverSize = true)
    {
        if (normalMap) return BCFormat.BC5;
        if (singleChannel) return BCFormat.BC4;
        if (hasAlpha) return qualityOverSize ? BCFormat.BC7 : BCFormat.BC3;
        return qualityOverSize ? BCFormat.BC7 : BCFormat.BC1;
    }

    public static uint ToFourCC(BCFormat fmt) => fmt switch
    {
        BCFormat.BC1 => FourCC_DXT1,
        BCFormat.BC1A => FourCC_DXT1,
        BCFormat.BC2 => FourCC_DXT3,
        BCFormat.BC3 => FourCC_DXT5,
        BCFormat.BC4 => FourCC_ATI1,
        BCFormat.BC5 => FourCC_ATI2,
        _ => 0,
    };

    public static byte ToRsc8(BCFormat fmt) => fmt switch
    {
        BCFormat.BC1 => (byte)Rsc8TextureFormat.BC1Unorm,
        BCFormat.BC1A => (byte)Rsc8TextureFormat.BC1Unorm,
        BCFormat.BC2 => (byte)Rsc8TextureFormat.BC2Unorm,
        BCFormat.BC3 => (byte)Rsc8TextureFormat.BC3Unorm,
        BCFormat.BC4 => (byte)Rsc8TextureFormat.BC4Unorm,
        BCFormat.BC5 => (byte)Rsc8TextureFormat.BC5Unorm,
        BCFormat.BC6H => (byte)Rsc8TextureFormat.BC6HUf16,
        BCFormat.BC7 => (byte)Rsc8TextureFormat.BC7Unorm,
        BCFormat.A8R8G8B8 => (byte)Rsc8TextureFormat.B8G8R8A8Unorm,
        BCFormat.R8G8B8A8 => (byte)Rsc8TextureFormat.R8G8B8A8Unorm,
        BCFormat.B5G6R5 => (byte)Rsc8TextureFormat.B5G6R5Unorm,
        BCFormat.B5G5R5A1 => (byte)Rsc8TextureFormat.B5G5R5A1Unorm,
        BCFormat.R10G10B10A2 => (byte)Rsc8TextureFormat.R10G10B10A2Unorm,
        BCFormat.R8 => (byte)Rsc8TextureFormat.R8Unorm,
        BCFormat.A8 => (byte)Rsc8TextureFormat.A8Unorm,
        BCFormat.R8G8 => (byte)Rsc8TextureFormat.R8G8Unorm,
        BCFormat.R16_FLOAT => (byte)Rsc8TextureFormat.R16Float,
        BCFormat.R16G16_FLOAT => (byte)Rsc8TextureFormat.R16G16Float,
        BCFormat.R16G16B16A16_FLOAT => (byte)Rsc8TextureFormat.R16G16B16A16Float,
        BCFormat.R32_FLOAT => (byte)Rsc8TextureFormat.R32Float,
        BCFormat.R32G32B32A32_FLOAT => (byte)Rsc8TextureFormat.R32G32B32A32Float,
        _ => throw new ArgumentOutOfRangeException(nameof(fmt)),
    };

    public static BCFormat FromRsc8(byte code) => code switch
    {
        (byte)Rsc8TextureFormat.BC1Unorm or (byte)Rsc8TextureFormat.BC1UnormSrgb => BCFormat.BC1,
        (byte)Rsc8TextureFormat.BC2Unorm or (byte)Rsc8TextureFormat.BC2UnormSrgb => BCFormat.BC2,
        (byte)Rsc8TextureFormat.BC3Unorm or (byte)Rsc8TextureFormat.BC3UnormSrgb => BCFormat.BC3,
        (byte)Rsc8TextureFormat.BC4Unorm => BCFormat.BC4,
        (byte)Rsc8TextureFormat.BC5Unorm => BCFormat.BC5,
        (byte)Rsc8TextureFormat.BC6HUf16 => BCFormat.BC6H,
        (byte)Rsc8TextureFormat.BC7Unorm or (byte)Rsc8TextureFormat.BC7UnormSrgb => BCFormat.BC7,
        (byte)Rsc8TextureFormat.B8G8R8A8Unorm or (byte)Rsc8TextureFormat.B8G8R8A8UnormSrgb => BCFormat.A8R8G8B8,
        (byte)Rsc8TextureFormat.R8G8B8A8Unorm or (byte)Rsc8TextureFormat.R8G8B8A8UnormSrgb => BCFormat.R8G8B8A8,
        (byte)Rsc8TextureFormat.B5G6R5Unorm => BCFormat.B5G6R5,
        (byte)Rsc8TextureFormat.B5G5R5A1Unorm => BCFormat.B5G5R5A1,
        (byte)Rsc8TextureFormat.R10G10B10A2Unorm => BCFormat.R10G10B10A2,
        (byte)Rsc8TextureFormat.R8Unorm => BCFormat.R8,
        (byte)Rsc8TextureFormat.A8Unorm => BCFormat.A8,
        (byte)Rsc8TextureFormat.R8G8Unorm => BCFormat.R8G8,
        (byte)Rsc8TextureFormat.R16Float => BCFormat.R16_FLOAT,
        (byte)Rsc8TextureFormat.R16G16Float => BCFormat.R16G16_FLOAT,
        (byte)Rsc8TextureFormat.R16G16B16A16Float => BCFormat.R16G16B16A16_FLOAT,
        (byte)Rsc8TextureFormat.R32Float => BCFormat.R32_FLOAT,
        (byte)Rsc8TextureFormat.R32G32B32A32Float => BCFormat.R32G32B32A32_FLOAT,
        _ => throw new ArgumentException($"Unsupported RSC8 format: 0x{code:X2}"),
    };

    public static uint ToRsc5(BCFormat fmt) => fmt switch
    {
        BCFormat.BC1 => (uint)Rsc5TextureFormat.DXT1,
        BCFormat.BC1A => (uint)Rsc5TextureFormat.DXT1,
        BCFormat.BC2 => (uint)Rsc5TextureFormat.DXT3,
        BCFormat.BC3 => (uint)Rsc5TextureFormat.DXT5,
        BCFormat.A8R8G8B8 => (uint)Rsc5TextureFormat.A8R8G8B8,
        BCFormat.B5G5R5A1 => (uint)Rsc5TextureFormat.A1R5G5B5,
        BCFormat.B5G6R5 => (uint)Rsc5TextureFormat.R5G6B5,
        BCFormat.A8 => (uint)Rsc5TextureFormat.A8,
        BCFormat.R8 => (uint)Rsc5TextureFormat.L8,
        _ => throw new ArgumentOutOfRangeException(nameof(fmt), $"{fmt} is not supported by GTA IV"),
    };

    public static BCFormat FromRsc5(uint code) => code switch
    {
        (uint)Rsc5TextureFormat.DXT1 => BCFormat.BC1,
        (uint)Rsc5TextureFormat.DXT3 => BCFormat.BC2,
        (uint)Rsc5TextureFormat.DXT5 => BCFormat.BC3,
        (uint)Rsc5TextureFormat.A8R8G8B8 => BCFormat.A8R8G8B8,
        (uint)Rsc5TextureFormat.A1R5G5B5 => BCFormat.B5G5R5A1,
        (uint)Rsc5TextureFormat.R5G6B5 => BCFormat.B5G6R5,
        (uint)Rsc5TextureFormat.A8 => BCFormat.A8,
        (uint)Rsc5TextureFormat.L8 => BCFormat.R8,
        _ => throw new ArgumentException($"Unsupported RSC5 format: 0x{code:X8}"),
    };

    public static int BlockStride(BCFormat fmt) => fmt switch
    {
        BCFormat.BC1 or BCFormat.BC1A or BCFormat.BC4 => 8,
        BCFormat.BC2 or BCFormat.BC3 or BCFormat.BC5 or BCFormat.BC6H or BCFormat.BC7 => 16,
        _ => PixelByteSize(fmt),
    };

    public static int BitsPerPixel(BCFormat fmt) => fmt switch
    {
        BCFormat.BC1 or BCFormat.BC1A or BCFormat.BC4 => 4,
        BCFormat.BC2 or BCFormat.BC3 or BCFormat.BC5 or BCFormat.BC6H or BCFormat.BC7 => 8,
        _ => PixelByteSize(fmt) * 8,
    };

    public static int BlockCount(BCFormat fmt, int width, int height, int depth, int mips,
                                  int? align = null)
    {
        int bs = BlockStride(fmt);
        int bp = IsBlockCompressed(fmt) ? 4 : 1;

        int bw = width, bh = height;
        if (mips > 1)
        {
            bw = 1; while (bw < width) bw *= 2;
            bh = 1; while (bh < height) bh *= 2;
        }

        int a = align ?? (bs == 1 ? 16 : 8);
        int bc = 0;
        for (int m = 0; m < mips; m++)
        {
            int bx = Math.Max(1, (bw + bp - 1) / bp);
            int by = Math.Max(1, (bh + bp - 1) / bp);
            bx += (a - (bx % a)) % a;
            by += (a - (by % a)) % a;
            bc += bx * by * depth;
            bw /= 2;
            bh /= 2;
        }
        return bc;
    }
}
