using TexFury;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace TexFury.Tests;

public class SmokeTests
{
    private static byte[] MakeGradient(int w, int h)
    {
        byte[] pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int off = (y * w + x) * 4;
            pixels[off] = (byte)(255 * x / (w - 1));     // R
            pixels[off + 1] = (byte)(255 * y / (h - 1)); // G
            pixels[off + 2] = (byte)(255 * (1 - (float)x / (w - 1))); // B
            pixels[off + 3] = 255; // A
        }
        return pixels;
    }

    [Fact]
    public void CompressBC7_RoundTrips()
    {
        var tex = Texture.FromPixels(MakeGradient(256, 256), 256, 256,
            format: BCFormat.BC7, name: "test_bc7");

        Assert.Equal(256, tex.Width);
        Assert.Equal(256, tex.Height);
        Assert.Equal(BCFormat.BC7, tex.Format);
        Assert.True(tex.MipCount > 1);
        Assert.Equal("test_bc7", tex.Name);

        string path = Path.GetTempFileName() + ".dds";
        try
        {
            tex.SaveDds(path);
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0);

            var loaded = Texture.FromDds(path, name: "test_bc7");
            Assert.Equal(tex.Width, loaded.Width);
            Assert.Equal(tex.Height, loaded.Height);
            Assert.Equal(tex.Format, loaded.Format);
            Assert.Equal(tex.MipCount, loaded.MipCount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CompressBC1_Works()
    {
        var tex = Texture.FromPixels(MakeGradient(128, 128), 128, 128,
            format: BCFormat.BC1, name: "test_bc1");

        Assert.Equal(BCFormat.BC1, tex.Format);
        Assert.True(tex.Data.Length > 0);
    }

    [Fact]
    public void CompressA8R8G8B8_Works()
    {
        var tex = Texture.FromPixels(MakeGradient(64, 64), 64, 64,
            format: BCFormat.A8R8G8B8, name: "test_uncompressed");

        Assert.Equal(BCFormat.A8R8G8B8, tex.Format);
        Assert.True(tex.Data.Length > 0);

        byte[] dds = tex.ToDdsBytes();
        Assert.Equal(0x20534444u, BitConverter.ToUInt32(dds, 0)); // DDS magic
    }

    [Fact]
    public void YtdRoundTrip()
    {
        var tex1 = Texture.FromPixels(MakeGradient(256, 256), 256, 256,
            format: BCFormat.BC7, name: "gradient_bc7");
        var tex2 = Texture.FromPixels(MakeGradient(128, 128), 128, 128,
            format: BCFormat.A8R8G8B8, name: "gradient_uncompressed");

        var ytd = new ItdFile();
        ytd.Add(tex1);
        ytd.Add(tex2);

        string path = Path.GetTempFileName() + ".ytd";
        try
        {
            ytd.Save(path);
            Assert.True(File.Exists(path));

            var loaded = ItdFile.Load(path);
            Assert.Equal(2, loaded.Count);

            var names = loaded.Textures.Select(t => t.Name).OrderBy(n => n).ToList();
            Assert.Contains("gradient_bc7", names);
            Assert.Contains("gradient_uncompressed", names);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WtdRoundTrip_Gta4()
    {
        var tex1 = Texture.FromPixels(MakeGradient(128, 128), 128, 128,
            format: BCFormat.BC1, name: "gradient_bc1");
        var tex2 = Texture.FromPixels(MakeGradient(64, 64), 64, 64,
            format: BCFormat.BC3, name: "gradient_bc3");

        var wtd = new ItdFile(Game.GtaIV);
        wtd.Add(tex1);
        wtd.Add(tex2);

        string path = Path.GetTempFileName() + ".wtd";
        try
        {
            wtd.Save(path);
            var loaded = ItdFile.Load(path);

            Assert.Equal(Game.GtaIV, loaded.Game);
            Assert.Equal(2, loaded.Count);
            Assert.Contains("gradient_bc1", loaded.Names());
            Assert.Contains("gradient_bc3", loaded.Names());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FromDdsBytes_LoadsWithoutTempFile()
    {
        var tex = Texture.FromPixels(MakeGradient(64, 64), 64, 64,
            format: BCFormat.BC7, name: "memory_dds");

        var loaded = Texture.FromDdsBytes(tex.ToDdsBytes(), name: "memory_dds");

        Assert.Equal(tex.Width, loaded.Width);
        Assert.Equal(tex.Height, loaded.Height);
        Assert.Equal(tex.Format, loaded.Format);
        Assert.Equal(tex.MipCount, loaded.MipCount);
    }

    [Fact]
    public void DdsBuilder_UsesExpectedHeadersForNewFormats()
    {
        byte[] bc1aData = new byte[128];
        var bc1a = Texture.FromRaw(bc1aData, 16, 16, BCFormat.BC1A, 1, [0], [bc1aData.Length], "bc1a");
        byte[] bc1aDds = bc1a.ToDdsBytes();
        Assert.Equal(0x31545844u, BitConverter.ToUInt32(bc1aDds, 84));

        byte[] bc2Data = new byte[256];
        var bc2 = Texture.FromRaw(bc2Data, 16, 16, BCFormat.BC2, 1, [0], [bc2Data.Length], "bc2");
        byte[] bc2Dds = bc2.ToDdsBytes();
        Assert.Equal(0x33545844u, BitConverter.ToUInt32(bc2Dds, 84));

        byte[] r8Data = new byte[256];
        var r8 = Texture.FromRaw(r8Data, 16, 16, BCFormat.R8, 1, [0], [r8Data.Length], "r8");
        byte[] r8Dds = r8.ToDdsBytes();
        Assert.Equal(0x30315844u, BitConverter.ToUInt32(r8Dds, 84));
        Assert.Equal(61u, BitConverter.ToUInt32(r8Dds, 128));
    }

    [Fact]
    public void ImageSharp_RoundTripsTexture()
    {
        using var image = Image.LoadPixelData<Rgba32>(MakeGradient(64, 64), 64, 64);
        var tex = image.ToTexFuryTexture(format: BCFormat.BC7, name: "imagesharp");

        Assert.Equal("imagesharp", tex.Name);
        Assert.Equal(BCFormat.BC7, tex.Format);

        using Image<Rgba32> decoded = tex.ToImageSharp();
        Assert.Equal(tex.Width, decoded.Width);
        Assert.Equal(tex.Height, decoded.Height);
    }

    [Fact]
    public void ImageSharp_DetectsTransparency()
    {
        byte[] pixels = MakeGradient(8, 8);
        pixels[3] = 0;

        using var image = Image.LoadPixelData<Rgba32>(pixels, 8, 8);

        Assert.True(TextureImageSharp.HasTransparency(image));
    }

    [Fact]
    public void ImageUtils_PowerOfTwo()
    {
        Assert.True(ImageUtils.IsPowerOfTwo(256, 512));
        Assert.False(ImageUtils.IsPowerOfTwo(300, 512));
        Assert.Equal(512, ImageUtils.NextPowerOfTwo(300));
        Assert.Equal((256, 512), ImageUtils.PotDimensions(200, 400));
    }

    [Fact]
    public void MipFilter_AllValues()
    {
        var pixels = MakeGradient(64, 64);
        foreach (MipFilter filter in Enum.GetValues<MipFilter>())
        {
            var tex = Texture.FromPixels(pixels, 64, 64,
                format: BCFormat.BC7, mipFilter: filter, name: $"test_{filter}");
            Assert.True(tex.MipCount > 1);
        }
    }

    [Fact]
    public void AllFormats_CompressAndSaveDds()
    {
        var pixels = MakeGradient(64, 64);
        BCFormat[] nativeEncodable =
        [
            BCFormat.BC1,
            BCFormat.BC3,
            BCFormat.BC4,
            BCFormat.BC5,
            BCFormat.BC7,
            BCFormat.A8R8G8B8,
        ];

        foreach (BCFormat fmt in nativeEncodable)
        {
            var tex = Texture.FromPixels(pixels, 64, 64, format: fmt, name: $"test_{fmt}");
            Assert.Equal(fmt, tex.Format);
            byte[] dds = tex.ToDdsBytes();
            Assert.True(dds.Length > 128);
        }
    }
}
