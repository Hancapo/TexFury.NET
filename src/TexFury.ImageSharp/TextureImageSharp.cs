using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TexFury;

/// <summary>ImageSharp integration helpers for TexFury textures.</summary>
public static class TextureImageSharp
{
    /// <summary>Create a TexFury texture from an ImageSharp image.</summary>
    public static Texture FromImageSharp(Image image,
        BCFormat format = BCFormat.BC1,
        float quality = 0.7f,
        bool generateMipmaps = true,
        int minMipSize = 4,
        bool resizeToPot = true,
        MipFilter mipFilter = MipFilter.Mitchell,
        string name = "")
    {
        using Image<Rgba32> rgba = image.CloneAs<Rgba32>();
        return FromImageSharp(rgba, format, quality, generateMipmaps,
            minMipSize, resizeToPot, mipFilter, name);
    }

    /// <summary>Create a TexFury texture from an ImageSharp RGBA image.</summary>
    public static Texture FromImageSharp(Image<Rgba32> image,
        BCFormat format = BCFormat.BC1,
        float quality = 0.7f,
        bool generateMipmaps = true,
        int minMipSize = 4,
        bool resizeToPot = true,
        MipFilter mipFilter = MipFilter.Mitchell,
        string name = "")
    {
        byte[] pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);
        return Texture.FromPixels(pixels, image.Width, image.Height, format,
            quality, generateMipmaps, minMipSize, resizeToPot, mipFilter, name);
    }

    /// <summary>Decompress a TexFury texture to an ImageSharp RGBA image.</summary>
    public static Image<Rgba32> ToImageSharp(this Texture texture, int mip = 0)
    {
        var (rgba, width, height) = texture.ToRgba(mip);
        return Image.LoadPixelData<Rgba32>(rgba, width, height);
    }

    /// <summary>True if an ImageSharp image has any non-opaque pixels.</summary>
    public static bool HasTransparency(Image image)
    {
        using Image<Rgba32> rgba = image.CloneAs<Rgba32>();
        return HasTransparency(rgba);
    }

    /// <summary>True if an ImageSharp RGBA image has any non-opaque pixels.</summary>
    public static bool HasTransparency(Image<Rgba32> image)
    {
        byte[] pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);

        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 255)
                return true;
        }

        return false;
    }

    /// <summary>Extension method alias for creating a TexFury texture from an ImageSharp image.</summary>
    public static Texture ToTexFuryTexture(this Image image,
        BCFormat format = BCFormat.BC1,
        float quality = 0.7f,
        bool generateMipmaps = true,
        int minMipSize = 4,
        bool resizeToPot = true,
        MipFilter mipFilter = MipFilter.Mitchell,
        string name = "") =>
        FromImageSharp(image, format, quality, generateMipmaps,
            minMipSize, resizeToPot, mipFilter, name);

    /// <summary>Extension method alias for creating a TexFury texture from an ImageSharp RGBA image.</summary>
    public static Texture ToTexFuryTexture(this Image<Rgba32> image,
        BCFormat format = BCFormat.BC1,
        float quality = 0.7f,
        bool generateMipmaps = true,
        int minMipSize = 4,
        bool resizeToPot = true,
        MipFilter mipFilter = MipFilter.Mitchell,
        string name = "") =>
        FromImageSharp(image, format, quality, generateMipmaps,
            minMipSize, resizeToPot, mipFilter, name);
}
