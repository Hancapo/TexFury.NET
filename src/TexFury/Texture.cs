using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace TexFury;

/// <summary>
/// A compressed DDS texture with optional mipmaps.
/// Create via static factory methods: FromImage, FromDds, FromRaw.
/// </summary>
public sealed class Texture
{
    public byte[] Data { get; }
    public int Width { get; }
    public int Height { get; }
    public BCFormat Format { get; }
    public int MipCount { get; }
    public int[] MipOffsets { get; }
    public int[] MipSizes { get; }
    public string Name { get; set; }

    public Texture(byte[] data, int width, int height, BCFormat format,
                   int mipCount, int[] mipOffsets, int[] mipSizes, string name = "")
    {
        Data = data;
        Width = width;
        Height = height;
        Format = format;
        MipCount = mipCount;
        MipOffsets = mipOffsets;
        MipSizes = mipSizes;
        Name = name;
    }

    // ── Factory methods ──────────────────────────────────────────────────

    /// <summary>Load an image file and compress it.</summary>
    public static Texture FromImage(string path,
        BCFormat format = BCFormat.BC7,
        float quality = 0.7f,
        bool generateMipmaps = true,
        int minMipSize = 4,
        bool resizeToPot = true,
        MipFilter mipFilter = MipFilter.Mitchell,
        string? name = null)
    {
        string fullPath = Path.GetFullPath(path);
        name ??= Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

        IntPtr img = NativeMethods.tf_load_image(fullPath);
        if (img == IntPtr.Zero)
            throw new FileNotFoundException($"Failed to load image: {path}");
        try
        {
            return CompressImage(img, format, quality, generateMipmaps,
                                 minMipSize, resizeToPot, mipFilter, name);
        }
        finally
        {
            NativeMethods.tf_free_image(img);
        }
    }

    /// <summary>Create a Texture from raw RGBA pixel data in memory.</summary>
    public static Texture FromPixels(byte[] rgbaData, int width, int height,
        BCFormat format = BCFormat.BC7,
        float quality = 0.7f,
        bool generateMipmaps = true,
        int minMipSize = 4,
        bool resizeToPot = true,
        MipFilter mipFilter = MipFilter.Mitchell,
        string name = "")
    {
        unsafe
        {
            fixed (byte* ptr = rgbaData)
            {
                IntPtr img = NativeMethods.tf_create_image(width, height, (IntPtr)ptr);
                if (img == IntPtr.Zero)
                    throw new InvalidOperationException("Failed to create image from pixel data");
                try
                {
                    return CompressImage(img, format, quality, generateMipmaps,
                                         minMipSize, resizeToPot, mipFilter, name);
                }
                finally
                {
                    NativeMethods.tf_free_image(img);
                }
            }
        }
    }

    /// <summary>Load an image from in-memory bytes and compress it.</summary>
    public static Texture FromBytes(byte[] data,
        BCFormat format = BCFormat.BC7,
        float quality = 0.7f,
        bool generateMipmaps = true,
        int minMipSize = 4,
        bool resizeToPot = true,
        MipFilter mipFilter = MipFilter.Mitchell,
        string name = "")
    {
        unsafe
        {
            fixed (byte* ptr = data)
            {
                IntPtr img = NativeMethods.tf_load_image_memory((IntPtr)ptr, (nuint)data.Length);
                if (img == IntPtr.Zero)
                    throw new InvalidOperationException("Failed to load image from bytes");
                try
                {
                    return CompressImage(img, format, quality, generateMipmaps,
                                         minMipSize, resizeToPot, mipFilter, name);
                }
                finally
                {
                    NativeMethods.tf_free_image(img);
                }
            }
        }
    }

    /// <summary>Load an existing DDS file.</summary>
    public static Texture FromDds(string path, string? name = null)
    {
        string fullPath = Path.GetFullPath(path);
        name ??= Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

        IntPtr c = NativeMethods.tf_load_dds(fullPath);
        if (c == IntPtr.Zero)
            throw new FileNotFoundException($"Failed to load DDS: {path}");
        try
        {
            return FromCompressedHandle(c, name);
        }
        finally
        {
            NativeMethods.tf_free_compressed(c);
        }
    }

    /// <summary>Create from raw compressed pixel data (for internal/advanced use).</summary>
    public static Texture FromRaw(byte[] data, int width, int height,
        BCFormat format, int mipCount, int[] mipOffsets, int[] mipSizes, string name = "")
    {
        return new Texture(data, width, height, format, mipCount, mipOffsets, mipSizes, name);
    }

    // ── Output ───────────────────────────────────────────────────────────

    /// <summary>Write this texture as a DDS file.</summary>
    public void SaveDds(string path) => File.WriteAllBytes(path, ToDdsBytes());

    /// <summary>Return complete DDS file as a byte array.</summary>
    public byte[] ToDdsBytes() =>
        DdsBuilder.Build(Width, Height, Format, MipCount, MipSizes, Data);

    // ── Decompression ───────────────────────────────────────────────────

    /// <summary>Decompress to raw RGBA pixels.</summary>
    /// <returns>(rgbaBytes, width, height) for the given mip level.</returns>
    public (byte[] Rgba, int Width, int Height) ToRgba(int mip = 0)
    {
        IntPtr c = ToCompressedHandle();
        try
        {
            IntPtr ptr = NativeMethods.tf_decompress(c, mip, out int w, out int h);
            if (ptr == IntPtr.Zero)
                throw new InvalidOperationException($"Failed to decompress mip {mip}");
            try
            {
                byte[] rgba = new byte[w * h * 4];
                Marshal.Copy(ptr, rgba, 0, rgba.Length);
                return (rgba, w, h);
            }
            finally
            {
                NativeMethods.tf_free_buffer(ptr);
            }
        }
        finally
        {
            NativeMethods.tf_free_compressed(c);
        }
    }

    // ── Quality metrics ─────────────────────────────────────────────────

    /// <summary>Compare this texture against original RGBA pixels.</summary>
    /// <returns>Dictionary with keys: PsnrRgb, PsnrRgba, Ssim.</returns>
    public QualityMetrics QualityMetrics(byte[] originalRgba)
    {
        var (decompressed, w, h) = ToRgba(0);

        unsafe
        {
            fixed (byte* origPtr = originalRgba)
            fixed (byte* decPtr = decompressed)
            {
                double psnrRgb = NativeMethods.tf_psnr((IntPtr)origPtr, (IntPtr)decPtr, w, h, 3);
                double psnrRgba = NativeMethods.tf_psnr((IntPtr)origPtr, (IntPtr)decPtr, w, h, 4);
                double ssim = NativeMethods.tf_ssim((IntPtr)origPtr, (IntPtr)decPtr, w, h);
                return new QualityMetrics(psnrRgb, psnrRgba, ssim);
            }
        }
    }

    // ── Validation ──────────────────────────────────────────────────────

    /// <summary>Check texture for common issues. Empty list means everything is OK.</summary>
    public List<string> Validate()
    {
        var warnings = new List<string>();

        if (Width <= 0 || Height <= 0)
            warnings.Add($"Invalid dimensions: {Width}x{Height}");

        if ((Width & (Width - 1)) != 0 || (Height & (Height - 1)) != 0)
            warnings.Add($"Non-power-of-two dimensions: {Width}x{Height}");

        if (Formats.IsBlockCompressed(Format) && (Width < 4 || Height < 4))
            warnings.Add($"Dimensions {Width}x{Height} below minimum 4x4 for {Format}");

        if (MipCount < 1)
            warnings.Add("No mip levels");

        int expected = Formats.TotalMipDataSize(Width, Height, Format, MipCount);
        if (Data.Length != expected)
            warnings.Add($"Data size mismatch: expected {expected} bytes, got {Data.Length} bytes");

        if (Width > 16384 || Height > 16384)
            warnings.Add($"Dimensions {Width}x{Height} exceed 16384 max");

        if (string.IsNullOrEmpty(Name))
            warnings.Add("Texture has no name");

        return warnings;
    }

    // ── Inspection ──────────────────────────────────────────────────────

    /// <summary>Read DDS metadata without loading pixel data.</summary>
    public static TextureInfo InspectDds(string path)
    {
        string fullPath = Path.GetFullPath(path);
        IntPtr c = NativeMethods.tf_load_dds(fullPath);
        if (c == IntPtr.Zero)
            throw new FileNotFoundException($"Failed to load DDS: {path}");
        try
        {
            var fmt = (BCFormat)NativeMethods.tf_compressed_format(c);
            return new TextureInfo(
                Path.GetFileNameWithoutExtension(path).ToLowerInvariant(),
                NativeMethods.tf_compressed_width(c),
                NativeMethods.tf_compressed_height(c),
                fmt,
                NativeMethods.tf_compressed_mip_count(c),
                (int)NativeMethods.tf_compressed_size(c));
        }
        finally
        {
            NativeMethods.tf_free_compressed(c);
        }
    }

    // ── Internal ─────────────────────────────────────────────────────────

    private IntPtr ToCompressedHandle()
    {
        string tmpPath = Path.GetTempFileName() + ".dds";
        try
        {
            File.WriteAllBytes(tmpPath, ToDdsBytes());
            IntPtr c = NativeMethods.tf_load_dds(tmpPath);
            if (c == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create compressed handle");
            return c;
        }
        finally
        {
            try { File.Delete(tmpPath); } catch { }
        }
    }

    private static Texture CompressImage(IntPtr img, BCFormat format, float quality,
        bool generateMipmaps, int minMipSize, bool resizeToPot,
        MipFilter mipFilter, string name)
    {
        IntPtr workImg = img;
        IntPtr resized = IntPtr.Zero;

        if (resizeToPot && NativeMethods.tf_is_power_of_two(
                NativeMethods.tf_image_width(img),
                NativeMethods.tf_image_height(img)) == 0)
        {
            resized = NativeMethods.tf_resize_to_pot(img, (int)mipFilter);
            workImg = resized;
        }

        try
        {
            IntPtr c = NativeMethods.tf_compress(workImg, (int)format,
                generateMipmaps ? 1 : 0, minMipSize, quality, (int)mipFilter);
            if (c == IntPtr.Zero)
                throw new InvalidOperationException("Compression failed");
            try
            {
                return FromCompressedHandle(c, name);
            }
            finally
            {
                NativeMethods.tf_free_compressed(c);
            }
        }
        finally
        {
            if (resized != IntPtr.Zero)
                NativeMethods.tf_free_image(resized);
        }
    }

    private static Texture FromCompressedHandle(IntPtr c, string name)
    {
        IntPtr dataPtr = NativeMethods.tf_compressed_data(c);
        int size = (int)NativeMethods.tf_compressed_size(c);
        int width = NativeMethods.tf_compressed_width(c);
        int height = NativeMethods.tf_compressed_height(c);
        var format = (BCFormat)NativeMethods.tf_compressed_format(c);
        int mipCount = NativeMethods.tf_compressed_mip_count(c);

        byte[] data = new byte[size];
        Marshal.Copy(dataPtr, data, 0, size);

        int[] offsets = new int[mipCount];
        int[] sizes = new int[mipCount];
        for (int i = 0; i < mipCount; i++)
        {
            offsets[i] = (int)NativeMethods.tf_compressed_mip_offset(c, i);
            sizes[i] = (int)NativeMethods.tf_compressed_mip_size(c, i);
        }

        return new Texture(data, width, height, format, mipCount, offsets, sizes, name);
    }

    public override string ToString() =>
        $"Texture(Name={Name}, {Width}x{Height}, Format={Format}, Mips={MipCount})";
}
