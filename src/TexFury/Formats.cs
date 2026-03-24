namespace TexFury;

/// <summary>DXGI_FORMAT values used in DDS DX10 headers.</summary>
internal enum DxgiFormat : uint
{
    BC1Unorm = 71,
    BC3Unorm = 77,
    BC4Unorm = 80,
    BC5Unorm = 83,
    B8G8R8A8Unorm = 87,
    BC7Unorm = 98,
}

/// <summary>Format constants and utility methods.</summary>
internal static class Formats
{
    // FourCC codes
    public const uint FourCC_DXT1 = 0x31545844;
    public const uint FourCC_DXT5 = 0x35545844;
    public const uint FourCC_ATI1 = 0x31495441;
    public const uint FourCC_ATI2 = 0x32495441;
    public const uint FourCC_BC7  = 0x20374342; // "BC7 "
    public const uint FourCC_DX10 = 0x30315844; // "DX10"

    // D3DFMT enum value
    public const uint D3DFMT_A8R8G8B8 = 21;

    public static bool IsBlockCompressed(BCFormat fmt) => fmt != BCFormat.A8R8G8B8;

    public static int BlockByteSize(BCFormat fmt) =>
        fmt is BCFormat.BC1 or BCFormat.BC4 ? 8 : 16;

    public static int MipDataSize(int width, int height, BCFormat fmt)
    {
        if (fmt == BCFormat.A8R8G8B8)
            return width * height * 4;
        int bw = Math.Max(1, (width + 3) / 4);
        int bh = Math.Max(1, (height + 3) / 4);
        return bw * bh * BlockByteSize(fmt);
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
        if (fmt == BCFormat.A8R8G8B8)
            return width * 4;
        int bw = Math.Max(1, (width + 3) / 4);
        return bw * BlockByteSize(fmt);
    }

    public static DxgiFormat ToDxgi(BCFormat fmt) => fmt switch
    {
        BCFormat.BC1 => DxgiFormat.BC1Unorm,
        BCFormat.BC3 => DxgiFormat.BC3Unorm,
        BCFormat.BC4 => DxgiFormat.BC4Unorm,
        BCFormat.BC5 => DxgiFormat.BC5Unorm,
        BCFormat.BC7 => DxgiFormat.BC7Unorm,
        BCFormat.A8R8G8B8 => DxgiFormat.B8G8R8A8Unorm,
        _ => throw new ArgumentOutOfRangeException(nameof(fmt)),
    };

    public static uint ToDx9(BCFormat fmt) => fmt switch
    {
        BCFormat.BC1 => FourCC_DXT1,
        BCFormat.BC3 => FourCC_DXT5,
        BCFormat.BC4 => FourCC_ATI1,
        BCFormat.BC5 => FourCC_ATI2,
        BCFormat.BC7 => FourCC_BC7,
        BCFormat.A8R8G8B8 => D3DFMT_A8R8G8B8,
        _ => throw new ArgumentOutOfRangeException(nameof(fmt)),
    };

    public static BCFormat FromDx9(uint code) => code switch
    {
        FourCC_DXT1 => BCFormat.BC1,
        FourCC_DXT5 => BCFormat.BC3,
        FourCC_ATI1 => BCFormat.BC4,
        FourCC_ATI2 => BCFormat.BC5,
        FourCC_BC7 => BCFormat.BC7,
        D3DFMT_A8R8G8B8 => BCFormat.A8R8G8B8,
        _ => throw new ArgumentException($"Unsupported DX9 format code: 0x{code:X8}"),
    };

    public static BCFormat FromDxgi(uint code) => code switch
    {
        (uint)DxgiFormat.BC1Unorm => BCFormat.BC1,
        (uint)DxgiFormat.BC3Unorm => BCFormat.BC3,
        (uint)DxgiFormat.BC4Unorm => BCFormat.BC4,
        (uint)DxgiFormat.BC5Unorm => BCFormat.BC5,
        (uint)DxgiFormat.BC7Unorm => BCFormat.BC7,
        (uint)DxgiFormat.B8G8R8A8Unorm => BCFormat.A8R8G8B8,
        _ => throw new ArgumentException($"Unsupported DXGI format: {code}"),
    };

    /// <summary>Suggest the best BCFormat based on image characteristics.</summary>
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
        BCFormat.BC3 => FourCC_DXT5,
        BCFormat.BC4 => FourCC_ATI1,
        BCFormat.BC5 => FourCC_ATI2,
        _ => 0,
    };
}
