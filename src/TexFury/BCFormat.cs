namespace TexFury;

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
}
