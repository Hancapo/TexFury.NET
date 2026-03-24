using System.Buffers.Binary;
using System.Text;

namespace TexFury;

/// <summary>YTD texture dictionary file.</summary>
public sealed class YtdFile
{
    private readonly List<Texture> _textures = [];

    public IReadOnlyList<Texture> Textures => _textures.AsReadOnly();

    public void Add(Texture texture)
    {
        if (string.IsNullOrEmpty(texture.Name))
            throw new ArgumentException("Texture must have a name before adding to YTD");
        _textures.Add(texture);
    }

    public void Save(string path) => File.WriteAllBytes(path, Build());

    public static YtdFile Load(string path) => Parse(File.ReadAllBytes(path));

    public int Count => _textures.Count;

    /// <summary>Get the list of texture names in this YTD.</summary>
    public List<string> Names() => _textures.Select(t => t.Name).ToList();

    /// <summary>Check if a texture with the given name exists.</summary>
    public bool Contains(string name) =>
        _textures.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Get a texture by name.</summary>
    public Texture Get(string name)
    {
        var tex = _textures.FirstOrDefault(t =>
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (tex == null)
            throw new KeyNotFoundException($"Texture '{name}' not found in YTD");
        return tex;
    }

    /// <summary>Replace an existing texture by name.</summary>
    public void Replace(string name, Texture texture)
    {
        for (int i = 0; i < _textures.Count; i++)
        {
            if (_textures[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                if (texture.Name != _textures[i].Name)
                    texture.Name = _textures[i].Name;
                _textures[i] = texture;
                return;
            }
        }
        throw new KeyNotFoundException($"Texture '{name}' not found in YTD");
    }

    /// <summary>Remove a texture by name.</summary>
    public bool Remove(string name)
    {
        int idx = _textures.FindIndex(t =>
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return false;
        _textures.RemoveAt(idx);
        return true;
    }

    /// <summary>Inspect a YTD file without fully loading pixel data.</summary>
    public static List<TextureInfo> Inspect(string path)
    {
        byte[] fileData = File.ReadAllBytes(path);
        var (virtualData, physicalData) = Resource.DecompressRsc7(fileData);

        int count = R16(virtualData, 0x28);
        long itemsPtr = R64(virtualData, 0x30);
        int itemsOff = V2O(itemsPtr);

        var result = new List<TextureInfo>();

        for (int i = 0; i < count; i++)
        {
            long texPtr = R64(virtualData, itemsOff + 8 * i);
            int texOff = V2O(texPtr);

            long namePtr = R64(virtualData, texOff + 0x28);
            int width = R16S(virtualData, texOff + 0x50);
            int height = R16S(virtualData, texOff + 0x52);
            uint formatVal = R32(virtualData, texOff + 0x58);
            int mipLevels = virtualData[texOff + 0x5D];

            int nameOff = V2O(namePtr);
            int nameEnd = Array.IndexOf(virtualData, (byte)0, nameOff);
            string name = Encoding.UTF8.GetString(virtualData, nameOff, nameEnd - nameOff);

            BCFormat? fmt = null;
            try { fmt = Formats.FromDx9(formatVal); } catch { }
            if (fmt == null) try { fmt = Formats.FromDxgi(formatVal); } catch { }

            string formatName = fmt.HasValue ? fmt.Value.ToString() : $"Unknown(0x{formatVal:X8})";
            int dataSize = fmt.HasValue
                ? Formats.TotalMipDataSize(width, height, fmt.Value, mipLevels)
                : 0;

            result.Add(new TextureInfo(name, width, height,
                fmt ?? BCFormat.BC7, mipLevels, dataSize));
        }

        return result;
    }

    public override string ToString()
    {
        var names = _textures.Select(t => t.Name);
        return $"YtdFile(Textures=[{string.Join(", ", names)}])";
    }

    // ── Constants ────────────────────────────────────────────────────────

    private const int GrcTextureSize = 0x90;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".tiff",
        ".tif", ".webp", ".psd", ".gif", ".hdr"
    };

    // ── JOAAT hash ───────────────────────────────────────────────────────

    private static uint Joaat(string text)
    {
        uint h = 0;
        foreach (char c in text.ToLowerInvariant())
        {
            h += c;
            h += h << 10;
            h ^= h >> 6;
        }
        h += h << 3;
        h ^= h >> 11;
        h += h << 15;
        return h;
    }

    private static int Align(int offset, int alignment) =>
        (offset + alignment - 1) & ~(alignment - 1);

    // ── Builder ──────────────────────────────────────────────────────────

    private static int LargeMipDataSize(int w, int h, BCFormat fmt, int levels)
    {
        int total = 0;
        for (int lvl = 0; lvl < levels; lvl++)
        {
            int mw = Math.Max(1, w >> lvl);
            int mh = Math.Max(1, h >> lvl);
            if (mw >= 16 && mh >= 16)
                total += Formats.MipDataSize(mw, mh, fmt);
        }
        return total;
    }

    private byte[] Build()
    {
        var entries = _textures.OrderBy(t => Joaat(t.Name)).ToList();
        int n = entries.Count;
        if (n == 0)
            throw new InvalidOperationException("Cannot create YTD with zero textures");

        // Phase 1: virtual data layout
        int dictSize = 0x40;
        int keysOffset = dictSize;
        int keysSize = 4 * n;

        int ptrsOffset = Align(keysOffset + keysSize, 16);
        int ptrsSize = 8 * n;

        int texturesOffset = Align(ptrsOffset + ptrsSize, 16);
        int texturesSize = GrcTextureSize * n;

        int namesOffset = texturesOffset + texturesSize;
        var nameBytesList = new List<byte[]>();
        var nameOffsets = new List<int>();
        int cur = namesOffset;
        foreach (var e in entries)
        {
            byte[] encoded = Encoding.UTF8.GetBytes(e.Name + "\0");
            nameOffsets.Add(cur);
            nameBytesList.Add(encoded);
            cur += encoded.Length;
        }

        int pagemapOffset = Align(cur, 16);
        int virtualSize = pagemapOffset + 0x10;

        // Phase 2: physical data layout
        var physOffsets = new List<int>();
        int physCur = 0;
        foreach (var e in entries)
        {
            physOffsets.Add(physCur);
            physCur += e.Data.Length;
        }

        // Phase 3: build virtual buffer
        byte[] vbuf = new byte[virtualSize];

        long keysVaddr = Resource.VirtualBase + keysOffset;
        long ptrsVaddr = Resource.VirtualBase + ptrsOffset;
        long pagemapVaddr = Resource.VirtualBase + pagemapOffset;

        W64(vbuf, 0x00, 0);
        W64(vbuf, 0x08, pagemapVaddr);
        W64(vbuf, 0x10, 0);
        W32(vbuf, 0x18, 1);
        W32(vbuf, 0x1C, 0);
        W64(vbuf, 0x20, keysVaddr);
        W16(vbuf, 0x28, (ushort)n);
        W16(vbuf, 0x2A, (ushort)n);
        W32(vbuf, 0x2C, 0);
        W64(vbuf, 0x30, ptrsVaddr);
        W16(vbuf, 0x38, (ushort)n);
        W16(vbuf, 0x3A, (ushort)n);
        W32(vbuf, 0x3C, 0);

        for (int i = 0; i < n; i++)
            W32(vbuf, keysOffset + 4 * i, Joaat(entries[i].Name));

        for (int i = 0; i < n; i++)
        {
            long texVaddr = Resource.VirtualBase + texturesOffset + GrcTextureSize * i;
            W64(vbuf, ptrsOffset + 8 * i, texVaddr);
        }

        for (int i = 0; i < n; i++)
        {
            var e = entries[i];
            int off = texturesOffset + GrcTextureSize * i;
            var fmt = e.Format;

            uint formatVal = Formats.ToDx9(fmt);
            int stride = Formats.RowPitch(e.Width, fmt);
            long nameVaddr = Resource.VirtualBase + nameOffsets[i];
            long dataPaddr = Resource.PhysicalBase + physOffsets[i];
            int dataSizeLarge = LargeMipDataSize(e.Width, e.Height, fmt, e.MipCount);

            W64(vbuf, off + 0x00, 0);
            W64(vbuf, off + 0x08, 0);
            W64(vbuf, off + 0x10, 0);
            W64(vbuf, off + 0x18, 0);
            W64(vbuf, off + 0x20, 0);
            W64(vbuf, off + 0x28, nameVaddr);
            W16(vbuf, off + 0x30, 1);
            vbuf[off + 0x32] = 0;
            vbuf[off + 0x33] = 0;
            W32(vbuf, off + 0x34, 0);
            W64(vbuf, off + 0x38, 0);
            W32(vbuf, off + 0x40, (uint)dataSizeLarge);
            W32(vbuf, off + 0x44, 0);
            W32(vbuf, off + 0x48, 0);
            W16(vbuf, off + 0x4C, 0);
            W16(vbuf, off + 0x4E, 0);
            W16(vbuf, off + 0x50, (ushort)e.Width);
            W16(vbuf, off + 0x52, (ushort)e.Height);
            W16(vbuf, off + 0x54, 1);
            W16(vbuf, off + 0x56, (ushort)stride);
            W32(vbuf, off + 0x58, formatVal);
            vbuf[off + 0x5C] = 0;
            vbuf[off + 0x5D] = (byte)e.MipCount;
            vbuf[off + 0x5E] = 0;
            vbuf[off + 0x5F] = 0;
            W64(vbuf, off + 0x60, 0);
            W64(vbuf, off + 0x68, 0);
            W64(vbuf, off + 0x70, dataPaddr);
            W64(vbuf, off + 0x78, 0);
            W64(vbuf, off + 0x80, 0);
            W64(vbuf, off + 0x88, 0);
        }

        for (int i = 0; i < nameBytesList.Count; i++)
        {
            byte[] nameData = nameBytesList[i];
            Array.Copy(nameData, 0, vbuf, nameOffsets[i], nameData.Length);
        }

        vbuf[pagemapOffset] = 1;
        vbuf[pagemapOffset + 1] = 1;

        // Phase 4: physical data
        byte[] pbuf = new byte[physCur];
        for (int i = 0; i < entries.Count; i++)
            Array.Copy(entries[i].Data, 0, pbuf, physOffsets[i], entries[i].Data.Length);

        return Resource.BuildRsc7(vbuf, pbuf);
    }

    // ── Parser ───────────────────────────────────────────────────────────

    private static YtdFile Parse(byte[] fileData)
    {
        var (virtualData, physicalData) = Resource.DecompressRsc7(fileData);

        int count = R16(virtualData, 0x28);
        long keysPtr = R64(virtualData, 0x20);
        long itemsPtr = R64(virtualData, 0x30);
        int keysOff = V2O(keysPtr);
        int itemsOff = V2O(itemsPtr);

        var ytd = new YtdFile();

        for (int i = 0; i < count; i++)
        {
            long texPtr = R64(virtualData, itemsOff + 8 * i);
            int texOff = V2O(texPtr);

            long namePtr = R64(virtualData, texOff + 0x28);
            int width = R16S(virtualData, texOff + 0x50);
            int height = R16S(virtualData, texOff + 0x52);
            uint formatVal = R32(virtualData, texOff + 0x58);
            int mipLevels = virtualData[texOff + 0x5D];
            long dataPtr = R64(virtualData, texOff + 0x70);

            int nameOff = V2O(namePtr);
            int nameEnd = Array.IndexOf(virtualData, (byte)0, nameOff);
            string name = Encoding.UTF8.GetString(virtualData, nameOff, nameEnd - nameOff);

            BCFormat fmt;
            try { fmt = Formats.FromDx9(formatVal); }
            catch
            {
                try { fmt = Formats.FromDxgi(formatVal); }
                catch { throw new InvalidDataException($"Unsupported texture format in YTD: 0x{formatVal:X8}"); }
            }

            int physOff = P2O(dataPtr);
            int dataSize = Formats.TotalMipDataSize(width, height, fmt, mipLevels);
            byte[] pixelData = new byte[dataSize];
            Array.Copy(physicalData, physOff, pixelData, 0, dataSize);

            int[] offsets = new int[mipLevels];
            int[] sizes = new int[mipLevels];
            int w = width, h = height, off = 0;
            for (int m = 0; m < mipLevels; m++)
            {
                int ms = Formats.MipDataSize(w, h, fmt);
                offsets[m] = off;
                sizes[m] = ms;
                off += ms;
                w = Math.Max(1, w / 2);
                h = Math.Max(1, h / 2);
            }

            ytd.Add(Texture.FromRaw(pixelData, width, height, fmt, mipLevels, offsets, sizes, name));
        }

        return ytd;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static int V2O(long addr) => (int)(addr - Resource.VirtualBase);
    private static int P2O(long addr) => (int)(addr - Resource.PhysicalBase);

    private static ushort R16(byte[] d, int o) => BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o));
    private static short R16S(byte[] d, int o) => BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(o));
    private static uint R32(byte[] d, int o) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o));
    private static long R64(byte[] d, int o) => (long)BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(o));

    private static void W16(byte[] d, int o, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(o), v);
    private static void W32(byte[] d, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(o), v);
    private static void W64(byte[] d, int o, long v) => BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(o), v);

    // ── High-level convenience methods ───────────────────────────────────

    /// <summary>Create a YTD from all images in a folder.</summary>
    public static string CreateFromFolder(string folder, string? output = null,
        BCFormat format = BCFormat.BC7, float quality = 0.7f,
        bool generateMipmaps = true, int minMipSize = 4,
        MipFilter mipFilter = MipFilter.Mitchell,
        Action<int, int, string>? onProgress = null)
    {
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Not a directory: {folder}");

        output ??= Path.Combine(Path.GetDirectoryName(folder)!,
                                Path.GetFileName(folder) + ".ytd");

        var files = Directory.GetFiles(folder)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f)) ||
                        Path.GetExtension(f).Equals(".dds", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0)
            throw new FileNotFoundException($"No image files found in {folder}");

        var ytd = new YtdFile();
        int total = files.Count;
        for (int i = 0; i < total; i++)
        {
            string name = Path.GetFileNameWithoutExtension(files[i]).ToLowerInvariant();
            onProgress?.Invoke(i + 1, total, name);

            Texture tex = Path.GetExtension(files[i]).Equals(".dds", StringComparison.OrdinalIgnoreCase)
                ? Texture.FromDds(files[i], name: name)
                : Texture.FromImage(files[i], format: format, quality: quality,
                    generateMipmaps: generateMipmaps, minMipSize: minMipSize,
                    mipFilter: mipFilter, name: name);

            ytd.Add(tex);
        }

        ytd.Save(output);
        return output;
    }

    /// <summary>Convert all images in a folder to DDS files.</summary>
    public static string BatchConvert(string folder, string? outputDir = null,
        BCFormat format = BCFormat.BC7, float quality = 0.7f,
        bool generateMipmaps = true, int minMipSize = 4,
        MipFilter mipFilter = MipFilter.Mitchell,
        Action<int, int, string>? onProgress = null)
    {
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Not a directory: {folder}");

        outputDir ??= Path.Combine(folder, "dds_out");
        Directory.CreateDirectory(outputDir);

        var files = Directory.GetFiles(folder)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0)
            throw new FileNotFoundException($"No image files found in {folder}");

        int total = files.Count;
        for (int i = 0; i < total; i++)
        {
            string name = Path.GetFileNameWithoutExtension(files[i]).ToLowerInvariant();
            onProgress?.Invoke(i + 1, total, name);

            var tex = Texture.FromImage(files[i], format: format, quality: quality,
                generateMipmaps: generateMipmaps, minMipSize: minMipSize,
                mipFilter: mipFilter, name: name);
            tex.SaveDds(Path.Combine(outputDir, name + ".dds"));
        }

        return outputDir;
    }

    /// <summary>Extract all textures from a YTD as DDS files.</summary>
    public static string ExtractYtd(string ytdPath, string? outputDir = null)
    {
        outputDir ??= Path.Combine(Path.GetDirectoryName(ytdPath)!,
                                    Path.GetFileNameWithoutExtension(ytdPath));
        Directory.CreateDirectory(outputDir);

        var ytd = YtdFile.Load(ytdPath);
        foreach (var tex in ytd.Textures)
            tex.SaveDds(Path.Combine(outputDir, tex.Name + ".dds"));

        return outputDir;
    }
}
