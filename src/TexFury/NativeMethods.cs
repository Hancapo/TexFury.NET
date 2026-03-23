using System.Runtime.InteropServices;

namespace TexFury;

/// <summary>P/Invoke bindings to texfury_native.dll.</summary>
internal static partial class NativeMethods
{
    private const string Lib = "texfury_native";

    static NativeMethods() => NativeResolver.EnsureRegistered();

    // Image lifecycle
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern IntPtr tf_load_image([MarshalAs(UnmanagedType.LPWStr)] string path);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr tf_load_image_memory(IntPtr data, nuint size);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr tf_create_image(int width, int height, IntPtr rgbaData);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void tf_free_image(IntPtr img);

    // Image queries
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int tf_image_width(IntPtr img);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int tf_image_height(IntPtr img);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int tf_image_channels(IntPtr img);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr tf_image_pixels(IntPtr img);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int tf_has_transparency(IntPtr img);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int tf_is_power_of_two(int width, int height);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int tf_next_power_of_two(int value);

    // Image transforms
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr tf_resize(IntPtr img, int width, int height, int filter);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr tf_resize_to_pot(IntPtr img, int filter);

    // Compression
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr tf_compress(IntPtr img, int format, int generateMipmaps,
                                            int minMipDim, float quality, int mipFilter);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void tf_free_compressed(IntPtr compressed);

    // Compressed data accessors
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr tf_compressed_data(IntPtr compressed);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint tf_compressed_size(IntPtr compressed);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int tf_compressed_width(IntPtr compressed);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int tf_compressed_height(IntPtr compressed);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int tf_compressed_format(IntPtr compressed);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int tf_compressed_mip_count(IntPtr compressed);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint tf_compressed_mip_offset(IntPtr compressed, int mip);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint tf_compressed_mip_size(IntPtr compressed, int mip);

    // DDS I/O
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern int tf_save_dds(IntPtr compressed, [MarshalAs(UnmanagedType.LPWStr)] string path);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int tf_save_dds_memory(IntPtr compressed, out IntPtr data, out nuint size);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern IntPtr tf_load_dds([MarshalAs(UnmanagedType.LPWStr)] string path);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void tf_free_buffer(IntPtr buffer);
}
