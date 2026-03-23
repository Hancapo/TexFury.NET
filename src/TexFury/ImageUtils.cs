using System.Runtime.InteropServices;

namespace TexFury;

/// <summary>Standalone image utility functions.</summary>
public static class ImageUtils
{
    /// <summary>Check if an image file has any transparent pixels.</summary>
    public static bool HasTransparency(string path)
    {
        string fullPath = Path.GetFullPath(path);
        IntPtr img = NativeMethods.tf_load_image(fullPath);
        if (img == IntPtr.Zero)
            throw new FileNotFoundException($"Failed to load image: {path}");
        try
        {
            return NativeMethods.tf_has_transparency(img) != 0;
        }
        finally
        {
            NativeMethods.tf_free_image(img);
        }
    }

    /// <summary>Check if both dimensions are powers of two.</summary>
    public static bool IsPowerOfTwo(int width, int height) =>
        NativeMethods.tf_is_power_of_two(width, height) != 0;

    /// <summary>Return the next power of two >= value.</summary>
    public static int NextPowerOfTwo(int value) =>
        NativeMethods.tf_next_power_of_two(value);

    /// <summary>Return the nearest power-of-two dimensions for the given size.</summary>
    public static (int Width, int Height) PotDimensions(int width, int height) =>
        (NativeMethods.tf_next_power_of_two(width), NativeMethods.tf_next_power_of_two(height));

    /// <summary>Get image dimensions and channel count.</summary>
    public static (int Width, int Height, int Channels) ImageDimensions(string path)
    {
        string fullPath = Path.GetFullPath(path);
        IntPtr img = NativeMethods.tf_load_image(fullPath);
        if (img == IntPtr.Zero)
            throw new FileNotFoundException($"Failed to load image: {path}");
        try
        {
            return (NativeMethods.tf_image_width(img),
                    NativeMethods.tf_image_height(img),
                    NativeMethods.tf_image_channels(img));
        }
        finally
        {
            NativeMethods.tf_free_image(img);
        }
    }
}
