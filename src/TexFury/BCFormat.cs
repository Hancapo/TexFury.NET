namespace TexFury;

/// <summary>Compression algorithm for RSC resource containers.</summary>
public enum RscCompression
{
    /// <summary>Raw DEFLATE stream.</summary>
    Deflate = 1,

    /// <summary>Oodle Kraken, used by vanilla RDR2 resources.</summary>
    Oodle = 2,
}

/// <summary>Block compression formats for DDS textures.</summary>
public enum BCFormat
{
    /// <summary>RGB, 6:1 compression (aka DXT1). No alpha.</summary>
    BC1 = 0,

    /// <summary>RGBA, 4:1 compression (aka DXT5). Full alpha.</summary>
    BC3 = 1,

    /// <summary>Single channel (R), 4:1 compression (aka ATI1).</summary>
    BC4 = 2,

    /// <summary>Two channels (RG), 4:1 compression (aka ATI2). Normal maps.</summary>
    BC5 = 3,

    /// <summary>RGBA, 4:1 compression. High quality, slower to encode.</summary>
    BC7 = 4,

    /// <summary>Uncompressed 32-bit BGRA.</summary>
    A8R8G8B8 = 5,

    /// <summary>RGBA, 4:1 compression (aka DXT3). Explicit 4-bit alpha.</summary>
    BC2 = 6,

    /// <summary>HDR RGB, half-float block compression.</summary>
    BC6H = 7,

    /// <summary>BC1/DXT1 with 1-bit punch-through alpha.</summary>
    BC1A = 8,

    /// <summary>Uncompressed 32-bit RGBA.</summary>
    R8G8B8A8 = 10,

    /// <summary>Uncompressed 16-bit RGB 5-6-5.</summary>
    B5G6R5 = 11,

    /// <summary>Uncompressed 16-bit BGRA 5-5-5-1.</summary>
    B5G5R5A1 = 12,

    /// <summary>Uncompressed 32-bit RGB 10-10-10 with 2-bit alpha.</summary>
    R10G10B10A2 = 13,

    /// <summary>Uncompressed 8-bit single channel.</summary>
    R8 = 20,

    /// <summary>Uncompressed 8-bit alpha-only channel.</summary>
    A8 = 21,

    /// <summary>Uncompressed 16-bit two-channel data.</summary>
    R8G8 = 22,

    /// <summary>16-bit half-float single channel.</summary>
    R16_FLOAT = 30,

    /// <summary>32-bit half-float two-channel data.</summary>
    R16G16_FLOAT = 31,

    /// <summary>64-bit half-float RGBA.</summary>
    R16G16B16A16_FLOAT = 32,

    /// <summary>32-bit float single channel.</summary>
    R32_FLOAT = 33,

    /// <summary>128-bit float RGBA.</summary>
    R32G32B32A32_FLOAT = 34,
}
