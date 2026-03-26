using System.Buffers.Binary;
using System.Text;

namespace TexFury;

/// <summary>
/// Internal Texture Dictionary — generic abstraction over RAGE texture
/// dictionary formats: .ytd (x64) and .wtd (x32) in the future.
/// </summary>
public sealed class ItdFile
{
    private readonly List<Texture> _textures = [];
    private readonly Game _game;

    public ItdFile(Game game = Game.GtaVLegacy) => _game = game;

    public Game Game => _game;
    public IReadOnlyList<Texture> Textures => _textures.AsReadOnly();
    public int Count => _textures.Count;

    public void Add(Texture texture)
    {
        if (string.IsNullOrEmpty(texture.Name))
            throw new ArgumentException("Texture must have a name before adding to ITD");
        _textures.Add(texture);
    }

    public List<string> Names() => _textures.Select(t => t.Name).ToList();

    public bool Contains(string name) =>
        _textures.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public Texture Get(string name)
    {
        var tex = _textures.FirstOrDefault(t =>
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return tex ?? throw new KeyNotFoundException($"Texture '{name}' not found");
    }

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
        throw new KeyNotFoundException($"Texture '{name}' not found");
    }

    public bool Remove(string name)
    {
        int idx = _textures.FindIndex(t =>
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return false;
        _textures.RemoveAt(idx);
        return true;
    }

    // ── Save / Load / Inspect ───────────────────────────────────────────

    public void Save(string path)
    {
        byte[] data = _game switch
        {
            Game.GtaVEnhanced => BuildEnhanced(),
            Game.Rdr2 => BuildRdr2(),
            _ => BuildGtaV(),
        };
        File.WriteAllBytes(path, data);
    }

    public static ItdFile Load(string path)
    {
        byte[] fileData = File.ReadAllBytes(path);
        Game game = DetectGame(fileData);
        return game switch
        {
            Game.GtaVEnhanced => ParseEnhanced(fileData),
            Game.Rdr2 => ParseRdr2(fileData),
            _ => ParseGtaV(fileData),
        };
    }

    public static List<TextureInfo> Inspect(string path)
    {
        byte[] fileData = File.ReadAllBytes(path);
        Game game = DetectGame(fileData);
        return game switch
        {
            Game.GtaVEnhanced => InspectEnhanced(fileData),
            Game.Rdr2 => InspectRdr2(fileData),
            _ => InspectGtaV(fileData),
        };
    }

    public override string ToString()
    {
        var names = _textures.Select(t => t.Name);
        return $"ItdFile(Game={_game}, Textures=[{string.Join(", ", names)}])";
    }

    // ── Detection ───────────────────────────────────────────────────────

    private static Game DetectGame(byte[] data)
    {
        if (data.Length < 16)
            throw new InvalidDataException("File too short to detect format");
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (magic == Resource.Rsc7Magic)
        {
            uint version = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
            return version == 5 ? Game.GtaVEnhanced : Game.GtaVLegacy;
        }
        if (magic == Rsc8.Rsc8Magic)
            return Game.Rdr2;
        throw new InvalidDataException($"Unknown format — magic: 0x{magic:X8}");
    }

    // ── Constants ────────────────────────────────────────────────────────

    private const int GrcTextureSize = 0x90;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".tiff",
        ".tif", ".webp", ".psd", ".gif", ".hdr"
    };

    // ── Shared helpers ──────────────────────────────────────────────────

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

    private static int V2O(long addr) => (int)(addr - Resource.VirtualBase);
    private static int P2O(long addr) => (int)(addr - Resource.PhysicalBase);

    private static ushort R16(byte[] d, int o) => BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o));
    private static short R16S(byte[] d, int o) => BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(o));
    private static uint R32(byte[] d, int o) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o));
    private static long R64(byte[] d, int o) => (long)BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(o));

    private static void W16(byte[] d, int o, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(o), v);
    private static void W32(byte[] d, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(o), v);
    private static void W64(byte[] d, int o, long v) => BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(o), v);

    private static string ReadName(byte[] virtualData, long namePtr)
    {
        int nameOff = V2O(namePtr);
        int nameEnd = Array.IndexOf(virtualData, (byte)0, nameOff);
        return Encoding.UTF8.GetString(virtualData, nameOff, nameEnd - nameOff);
    }

    private static (int[] offsets, int[] sizes) BuildMipInfo(int width, int height, BCFormat fmt, int mipCount)
    {
        int[] offsets = new int[mipCount];
        int[] sizes = new int[mipCount];
        int w = width, h = height, off = 0;
        for (int m = 0; m < mipCount; m++)
        {
            int ms = Formats.MipDataSize(w, h, fmt);
            offsets[m] = off;
            sizes[m] = ms;
            off += ms;
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        return (offsets, sizes);
    }

    // ═════════════════════════════════════════════════════════════════════
    // GTA V Legacy (RSC7 version 13)
    // ═════════════════════════════════════════════════════════════════════

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

    private byte[] BuildGtaV()
    {
        var entries = _textures.OrderBy(t => Joaat(t.Name)).ToList();
        int n = entries.Count;
        if (n == 0)
            throw new InvalidOperationException("Cannot create ITD with zero textures");

        int dictSize = 0x40;
        int keysOffset = dictSize;
        int ptrsOffset = Align(keysOffset + 4 * n, 16);
        int texturesOffset = Align(ptrsOffset + 8 * n, 16);

        int cur = texturesOffset + GrcTextureSize * n;
        var nameOffsets = new List<int>();
        var nameBytesList = new List<byte[]>();
        foreach (var e in entries)
        {
            nameOffsets.Add(cur);
            byte[] encoded = Encoding.UTF8.GetBytes(e.Name + "\0");
            nameBytesList.Add(encoded);
            cur += encoded.Length;
        }

        int pagemapOffset = Align(cur, 16);
        int virtualSize = pagemapOffset + 0x10;

        var physOffsets = new List<int>();
        int physCur = 0;
        foreach (var e in entries)
        {
            physOffsets.Add(physCur);
            physCur += e.Data.Length;
        }

        byte[] vbuf = new byte[virtualSize];

        W64(vbuf, 0x00, 0);
        W64(vbuf, 0x08, Resource.VirtualBase + pagemapOffset);
        W64(vbuf, 0x10, 0);
        W32(vbuf, 0x18, 1);
        W32(vbuf, 0x1C, 0);
        W64(vbuf, 0x20, Resource.VirtualBase + keysOffset);
        W16(vbuf, 0x28, (ushort)n);
        W16(vbuf, 0x2A, (ushort)n);
        W32(vbuf, 0x2C, 0);
        W64(vbuf, 0x30, Resource.VirtualBase + ptrsOffset);
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

            uint formatVal = Formats.ToDx9(e.Format);
            int stride = Formats.RowPitch(e.Width, e.Format);
            long nameVaddr = Resource.VirtualBase + nameOffsets[i];
            long dataPaddr = Resource.PhysicalBase + physOffsets[i];
            int dataSizeLarge = LargeMipDataSize(e.Width, e.Height, e.Format, e.MipCount);

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
            Array.Copy(nameBytesList[i], 0, vbuf, nameOffsets[i], nameBytesList[i].Length);

        vbuf[pagemapOffset] = 1;
        vbuf[pagemapOffset + 1] = 1;

        byte[] pbuf = new byte[physCur];
        for (int i = 0; i < entries.Count; i++)
            Array.Copy(entries[i].Data, 0, pbuf, physOffsets[i], entries[i].Data.Length);

        return Resource.BuildRsc7(vbuf, pbuf);
    }

    private static ItdFile ParseGtaV(byte[] fileData)
    {
        var (virtualData, physicalData) = Resource.DecompressRsc7(fileData);

        int count = R16(virtualData, 0x28);
        int itemsOff = V2O(R64(virtualData, 0x30));

        var itd = new ItdFile(Game.GtaVLegacy);

        for (int i = 0; i < count; i++)
        {
            int texOff = V2O(R64(virtualData, itemsOff + 8 * i));

            string name = ReadName(virtualData, R64(virtualData, texOff + 0x28));
            int width = R16S(virtualData, texOff + 0x50);
            int height = R16S(virtualData, texOff + 0x52);
            uint formatVal = R32(virtualData, texOff + 0x58);
            int mipLevels = virtualData[texOff + 0x5D];
            long dataPtr = R64(virtualData, texOff + 0x70);

            BCFormat fmt;
            try { fmt = Formats.FromDx9(formatVal); }
            catch
            {
                try { fmt = Formats.FromDxgi(formatVal); }
                catch { throw new InvalidDataException($"Unsupported format: 0x{formatVal:X8}"); }
            }

            int physOff = P2O(dataPtr);
            int dataSize = Formats.TotalMipDataSize(width, height, fmt, mipLevels);
            byte[] pixelData = new byte[dataSize];
            Array.Copy(physicalData, physOff, pixelData, 0, dataSize);

            var (offsets, sizes) = BuildMipInfo(width, height, fmt, mipLevels);
            itd.Add(Texture.FromRaw(pixelData, width, height, fmt, mipLevels, offsets, sizes, name));
        }

        return itd;
    }

    private static List<TextureInfo> InspectGtaV(byte[] fileData)
    {
        var (virtualData, _) = Resource.DecompressRsc7(fileData);

        int count = R16(virtualData, 0x28);
        int itemsOff = V2O(R64(virtualData, 0x30));

        var result = new List<TextureInfo>();
        for (int i = 0; i < count; i++)
        {
            int texOff = V2O(R64(virtualData, itemsOff + 8 * i));

            string name = ReadName(virtualData, R64(virtualData, texOff + 0x28));
            int width = R16S(virtualData, texOff + 0x50);
            int height = R16S(virtualData, texOff + 0x52);
            uint formatVal = R32(virtualData, texOff + 0x58);
            int mipLevels = virtualData[texOff + 0x5D];

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

    // ═════════════════════════════════════════════════════════════════════
    // RDR2 (RSC8) — placeholder, implemented in next commit
    // ═════════════════════════════════════════════════════════════════════

    private byte[] BuildRdr2() =>
        throw new NotImplementedException("RDR2 build not yet implemented");

    private static ItdFile ParseRdr2(byte[] fileData) =>
        throw new NotImplementedException("RDR2 parse not yet implemented");

    private static List<TextureInfo> InspectRdr2(byte[] fileData) =>
        throw new NotImplementedException("RDR2 inspect not yet implemented");

    // ═════════════════════════════════════════════════════════════════════
    // GTA V Enhanced — placeholder, implemented in later commit
    // ═════════════════════════════════════════════════════════════════════

    private byte[] BuildEnhanced() =>
        throw new NotImplementedException("Enhanced build not yet implemented");

    private static ItdFile ParseEnhanced(byte[] fileData) =>
        throw new NotImplementedException("Enhanced parse not yet implemented");

    private static List<TextureInfo> InspectEnhanced(byte[] fileData) =>
        throw new NotImplementedException("Enhanced inspect not yet implemented");

    // ── High-level convenience methods ───────────────────────────────────

    /// <summary>Create an ITD from all images in a folder.</summary>
    public static string CreateFromFolder(string folder, string? output = null,
        Game game = Game.GtaVLegacy,
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

        var itd = new ItdFile(game);
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

            itd.Add(tex);
        }

        itd.Save(output);
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

    /// <summary>Extract all textures from an ITD as DDS files.</summary>
    public static string Extract(string path, string? outputDir = null)
    {
        outputDir ??= Path.Combine(Path.GetDirectoryName(path)!,
                                    Path.GetFileNameWithoutExtension(path));
        Directory.CreateDirectory(outputDir);

        var itd = ItdFile.Load(path);
        foreach (var tex in itd.Textures)
            tex.SaveDds(Path.Combine(outputDir, tex.Name + ".dds"));

        return outputDir;
    }
}
