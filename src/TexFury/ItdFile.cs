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
    private Game _game;

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

    public void Merge(ItdFile other, bool overwrite = false)
    {
        var existing = new HashSet<string>(_textures.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var tex in other.Textures)
        {
            if (existing.Contains(tex.Name))
            {
                if (overwrite)
                    Replace(tex.Name, tex);
                continue;
            }

            _textures.Add(tex);
            existing.Add(tex.Name);
        }
    }

    public static ItdFile MergeMany(IEnumerable<string> paths, Game? game = null, bool overwrite = false)
    {
        var list = paths.ToList();
        if (list.Count == 0)
            throw new ArgumentException("paths must not be empty", nameof(paths));

        var result = Load(list[0]);
        if (game.HasValue)
            result._game = game.Value;

        for (int i = 1; i < list.Count; i++)
            result.Merge(Load(list[i]), overwrite);

        return result;
    }

    public List<Dictionary<string, object>> Convert(Game game,
        float quality = 0.7f,
        bool generateMipmaps = true,
        int minMipSize = 4,
        MipFilter mipFilter = MipFilter.Mitchell)
    {
        var report = new List<Dictionary<string, object>>();
        if (game != Game.GtaIV)
            return SetGameAndReturn(game, report);

        for (int i = 0; i < _textures.Count; i++)
        {
            var tex = _textures[i];
            if (Formats.IsGta4Supported(tex.Format))
                continue;

            BCFormat newFormat = tex.Format switch
            {
                BCFormat.BC4 or BCFormat.BC5 or BCFormat.BC6H => BCFormat.BC1,
                BCFormat.BC7 => tex.HasTransparency() ? BCFormat.BC3 : BCFormat.BC1,
                BCFormat.R8G8B8A8 or BCFormat.R10G10B10A2 or
                    BCFormat.R16G16B16A16_FLOAT or BCFormat.R32G32B32A32_FLOAT => BCFormat.A8R8G8B8,
                BCFormat.R8G8 or BCFormat.R16G16_FLOAT => BCFormat.R8,
                BCFormat.R16_FLOAT or BCFormat.R32_FLOAT => BCFormat.R8,
                _ => BCFormat.BC1,
            };

            _textures[i] = tex.ToFormat(newFormat, quality, generateMipmaps, minMipSize, mipFilter);
            report.Add(new Dictionary<string, object>
            {
                ["name"] = tex.Name,
                ["old_format"] = tex.Format.ToString(),
                ["new_format"] = newFormat.ToString(),
            });
        }

        return SetGameAndReturn(game, report);
    }

    private List<Dictionary<string, object>> SetGameAndReturn(Game game, List<Dictionary<string, object>> report)
    {
        _game = game;
        return report;
    }

    public List<Dictionary<string, object>> FixTextures(
        float quality = 0.7f,
        int minMipSize = 4,
        MipFilter mipFilter = MipFilter.Mitchell,
        ISet<string>? ignore = null,
        Action<int, int, string>? onProgress = null)
    {
        var report = new List<Dictionary<string, object>>();
        var skip = ignore is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(ignore, StringComparer.OrdinalIgnoreCase);

        int total = _textures.Count;
        for (int idx = 0; idx < total; idx++)
        {
            var tex = _textures[idx];
            onProgress?.Invoke(idx + 1, total, tex.Name);

            if (skip.Contains(tex.Name))
                continue;

            var fixes = new List<string>();
            int expectedMips = 1;
            int dim = Math.Max(tex.Width, tex.Height);
            while (dim > minMipSize)
            {
                dim /= 2;
                expectedMips++;
            }

            bool needsPot = !tex.IsPowerOfTwo;
            bool needsMips = tex.MipCount < expectedMips && Math.Max(tex.Width, tex.Height) >= 8;
            bool needsFormat = false;
            BCFormat suggestedFormat = tex.Format;

            if (Formats.IsBlockCompressed(tex.Format))
            {
                bool transparent = tex.HasTransparency();
                if (transparent && tex.Format == BCFormat.BC1)
                {
                    suggestedFormat = BCFormat.BC3;
                    needsFormat = true;
                    fixes.Add("format BC1->BC3 (has transparency)");
                }
                else if (!transparent && tex.Format is BCFormat.BC1A or BCFormat.BC3 or BCFormat.BC7)
                {
                    suggestedFormat = BCFormat.BC1;
                    needsFormat = true;
                    fixes.Add($"format {tex.Format}->BC1 (opaque)");
                }
            }

            if (needsPot)
                fixes.Add("resize to power-of-two");
            if (needsMips)
                fixes.Add($"mipmaps {tex.MipCount}->{expectedMips}");

            if (!needsPot && !needsMips && !needsFormat)
                continue;

            var (rgba, w, h) = tex.ToRgba(0);
            _textures[idx] = Texture.FromPixels(rgba, w, h, suggestedFormat,
                quality, generateMipmaps: true, minMipSize: minMipSize,
                resizeToPot: true, mipFilter: mipFilter, name: tex.Name);

            report.Add(new Dictionary<string, object>
            {
                ["name"] = tex.Name,
                ["fixes"] = fixes,
            });
        }

        return report;
    }

    // ── Save / Load / Inspect ───────────────────────────────────────────

    public void Save(string path, RscCompression? compression = null)
    {
        byte[] data = _game switch
        {
            Game.GtaIV => BuildGta4(),
            Game.GtaVEnhanced => BuildEnhanced(),
            Game.Rdr2 => BuildRdr2(compression ?? RscCompression.Oodle),
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
            Game.GtaIV => ParseGta4(fileData),
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
            Game.GtaIV => InspectGta4(fileData),
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
        if (magic == Rsc5.Rsc5Magic)
            return Game.GtaIV;
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

    private static byte[] SliceTextureData(byte[] physicalData, int physOff, int dataSize,
        string name, int width, int height, int mipLevels)
    {
        if (width <= 0 || height <= 0)
            throw new InvalidDataException($"Invalid dimensions for '{name}': {width}x{height}");
        if (mipLevels < 1)
            throw new InvalidDataException($"Invalid mip count for '{name}': {mipLevels}");
        if (physOff < 0 || dataSize < 0 || physOff + dataSize > physicalData.Length)
            throw new InvalidDataException(
                $"Texture data for '{name}' is outside the physical buffer (offset={physOff}, size={dataSize}, buffer={physicalData.Length})");

        byte[] pixelData = new byte[dataSize];
        Array.Copy(physicalData, physOff, pixelData, 0, dataSize);
        return pixelData;
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
            byte[] pixelData = SliceTextureData(physicalData, physOff, dataSize,
                name, width, height, mipLevels);

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
    // RDR2 (RSC8)
    // ═════════════════════════════════════════════════════════════════════

    private const int Rdr2TexSize = 0xB0; // 176 bytes
    private const long Rdr2DictVft = 0x00000001409100B0;
    private const long Rdr2TexVft = 0x00000001409100B0;
    private const long Rdr2SrvVft = 0x0000000140910080;
    private const uint Rdr2Flags = 0x18008002;
    private const byte Rdr2TileStandard = 13;
    private const byte Rdr2Dim2D = 1;
    private const long Rdr2SrvDim2D = 0x0401;

    private byte[] BuildRdr2(RscCompression compression = RscCompression.Oodle)
    {
        var entries = _textures.OrderBy(t => Joaat(t.Name)).ToList();
        int n = entries.Count;
        if (n == 0)
            throw new InvalidOperationException("Cannot create ITD with zero textures");

        // Virtual layout
        int dictSize = 64;
        int blockmapOff = Align(dictSize, 16);
        int blockmapSize = 16 + 2 * 8;

        int hashOff = Align(blockmapOff + blockmapSize, 16);
        int ptrOff = Align(hashOff + n * 4, 16);
        int texOffBase = Align(ptrOff + n * 8, 16);

        int cur = Align(texOffBase + Rdr2TexSize * n, 16);
        var nameOffsets = new List<int>();
        var nameBytesList = new List<byte[]>();
        foreach (var e in entries)
        {
            nameOffsets.Add(cur);
            byte[] encoded = Encoding.UTF8.GetBytes(e.Name + "\0");
            nameBytesList.Add(encoded);
            cur = Align(cur + encoded.Length, 16);
        }
        int virtualSize = cur;

        // Physical layout (padded to BlockCount * BlockStride)
        var physOffsets = new List<int>();
        var physDataList = new List<byte[]>();
        int physCur = 0;
        foreach (var e in entries)
        {
            physOffsets.Add(physCur);
            int bc = Formats.BlockCount(e.Format, e.Width, e.Height, 1, e.MipCount);
            int target = bc * Formats.BlockStride(e.Format);
            byte[] data = e.Data;
            if (data.Length < target)
            {
                byte[] padded = new byte[target];
                Array.Copy(data, padded, data.Length);
                data = padded;
            }
            physDataList.Add(data);
            physCur = Align(physCur + data.Length, 16);
        }
        int physicalSize = physCur;

        // Page sizes for BlockMap
        int vPage = Align(virtualSize, virtualSize > 0x8000 ? 0x10000 : 16);
        int pPage = Align(physicalSize, physicalSize > 0x8000 ? 0x10000 : 16);

        // Build virtual buffer
        byte[] vbuf = new byte[virtualSize];

        // Dictionary root (64 bytes)
        W64(vbuf, 0x00, Rdr2DictVft);
        W64(vbuf, 0x08, Resource.VirtualBase + blockmapOff);
        W64(vbuf, 0x10, 0);
        W64(vbuf, 0x18, 1);
        W64(vbuf, 0x20, Resource.VirtualBase + hashOff);
        W16(vbuf, 0x28, (ushort)n);
        W16(vbuf, 0x2A, (ushort)n);
        W32(vbuf, 0x2C, 0);
        W64(vbuf, 0x30, Resource.VirtualBase + ptrOff);
        W16(vbuf, 0x38, (ushort)n);
        W16(vbuf, 0x3A, (ushort)n);
        W32(vbuf, 0x3C, 0);

        // BlockMap
        W64(vbuf, blockmapOff, 0);
        vbuf[blockmapOff + 8] = 1; // virtual page count
        vbuf[blockmapOff + 9] = 1; // physical page count
        W16(vbuf, blockmapOff + 10, 0);
        W32(vbuf, blockmapOff + 12, 0);
        W64(vbuf, blockmapOff + 16, vPage);
        W64(vbuf, blockmapOff + 24, pPage);

        // Hash array
        for (int i = 0; i < n; i++)
            W32(vbuf, hashOff + 4 * i, Joaat(entries[i].Name));

        // Pointer array
        for (int i = 0; i < n; i++)
            W64(vbuf, ptrOff + 8 * i, Resource.VirtualBase + texOffBase + Rdr2TexSize * i);

        // Texture blocks (176 bytes each)
        for (int i = 0; i < n; i++)
        {
            var e = entries[i];
            int off = texOffBase + Rdr2TexSize * i;
            int bc = Formats.BlockCount(e.Format, e.Width, e.Height, 1, e.MipCount);
            int bs = Formats.BlockStride(e.Format);

            // TextureBase (0x00–0x4F)
            W64(vbuf, off + 0x00, Rdr2TexVft);
            W32(vbuf, off + 0x08, (uint)bc);
            W32(vbuf, off + 0x0C, (uint)bs);
            W32(vbuf, off + 0x10, Rdr2Flags);
            W32(vbuf, off + 0x14, 0);
            W16(vbuf, off + 0x18, (ushort)e.Width);
            W16(vbuf, off + 0x1A, (ushort)e.Height);
            W16(vbuf, off + 0x1C, 1); // depth
            vbuf[off + 0x1E] = Rdr2Dim2D;
            vbuf[off + 0x1F] = Formats.ToRsc8(e.Format);
            vbuf[off + 0x20] = Rdr2TileStandard;
            vbuf[off + 0x21] = 0; // AntiAliasType
            vbuf[off + 0x22] = (byte)e.MipCount;
            vbuf[off + 0x23] = 0;
            vbuf[off + 0x24] = 0;
            vbuf[off + 0x25] = 0;
            W16(vbuf, off + 0x26, 1); // UsageCount
            W64(vbuf, off + 0x28, Resource.VirtualBase + nameOffsets[i]);
            W64(vbuf, off + 0x30, Resource.VirtualBase + off + 0x68); // SRV ptr
            W64(vbuf, off + 0x38, Resource.PhysicalBase + physOffsets[i]);
            W32(vbuf, off + 0x40, 0);
            W32(vbuf, off + 0x44, 0);
            W64(vbuf, off + 0x48, 0);

            // Extended (0x50–0x67)
            W64(vbuf, off + 0x50, 0);
            W64(vbuf, off + 0x58, 0);
            W64(vbuf, off + 0x60, 0);

            // Embedded SRV (0x68–0xAF)
            W64(vbuf, off + 0x68, Rdr2SrvVft);
            W64(vbuf, off + 0x70, 0);
            W64(vbuf, off + 0x78, Rdr2SrvDim2D);
            W64(vbuf, off + 0x80, 5);
            W64(vbuf, off + 0x88, 0);
            W64(vbuf, off + 0x90, 0);
            W64(vbuf, off + 0x98, 0);
            W64(vbuf, off + 0xA0, 0);
            W64(vbuf, off + 0xA8, 0);
        }

        // Name strings
        for (int i = 0; i < nameBytesList.Count; i++)
            Array.Copy(nameBytesList[i], 0, vbuf, nameOffsets[i], nameBytesList[i].Length);

        // Physical buffer
        byte[] pbuf = new byte[physicalSize];
        for (int i = 0; i < physDataList.Count; i++)
            Array.Copy(physDataList[i], 0, pbuf, physOffsets[i], physDataList[i].Length);

        return Rsc8.BuildRsc8(vbuf, pbuf, compression: compression);
    }

    private static ItdFile ParseRdr2(byte[] fileData)
    {
        var (virtualData, physicalData) = Rsc8.DecompressRsc8(fileData);

        int count = R16(virtualData, 0x28);
        int itemsOff = V2O(R64(virtualData, 0x30));

        var itd = new ItdFile(Game.Rdr2);

        for (int i = 0; i < count; i++)
        {
            int texOff = V2O(R64(virtualData, itemsOff + 8 * i));

            string name = ReadName(virtualData, R64(virtualData, texOff + 0x28));
            int width = R16(virtualData, texOff + 0x18);
            int height = R16(virtualData, texOff + 0x1A);
            byte formatByte = virtualData[texOff + 0x1F];
            int mipLevels = virtualData[texOff + 0x22];
            long dataPtr = R64(virtualData, texOff + 0x38);

            BCFormat fmt = Formats.FromRsc8(formatByte);
            int physOff = P2O(dataPtr);
            int dataSize = Formats.TotalMipDataSize(width, height, fmt, mipLevels);
            byte[] pixelData = SliceTextureData(physicalData, physOff, dataSize,
                name, width, height, mipLevels);

            var (offsets, sizes) = BuildMipInfo(width, height, fmt, mipLevels);
            itd.Add(Texture.FromRaw(pixelData, width, height, fmt, mipLevels, offsets, sizes, name));
        }

        return itd;
    }

    private static List<TextureInfo> InspectRdr2(byte[] fileData)
    {
        var (virtualData, _) = Rsc8.DecompressRsc8(fileData);

        int count = R16(virtualData, 0x28);
        int itemsOff = V2O(R64(virtualData, 0x30));

        var result = new List<TextureInfo>();
        for (int i = 0; i < count; i++)
        {
            int texOff = V2O(R64(virtualData, itemsOff + 8 * i));

            string name = ReadName(virtualData, R64(virtualData, texOff + 0x28));
            int width = R16(virtualData, texOff + 0x18);
            int height = R16(virtualData, texOff + 0x1A);
            byte formatByte = virtualData[texOff + 0x1F];
            int mipLevels = virtualData[texOff + 0x22];

            BCFormat? fmt = null;
            try { fmt = Formats.FromRsc8(formatByte); } catch { }

            string formatName = fmt.HasValue ? fmt.Value.ToString() : $"Unknown(0x{formatByte:X2})";
            int dataSize = fmt.HasValue
                ? Formats.TotalMipDataSize(width, height, fmt.Value, mipLevels) : 0;

            result.Add(new TextureInfo(name, width, height,
                fmt ?? BCFormat.BC7, mipLevels, dataSize));
        }

        return result;
    }

    // ═════════════════════════════════════════════════════════════════════
    // GTA V Enhanced / gen9 (RSC7 version 5)
    // ═════════════════════════════════════════════════════════════════════

    private const int EnhancedTexSize = 0x80; // 128 bytes
    private const uint EnhancedFlags = 0x00260208;
    private const byte EnhancedTileAuto = 255;
    private const byte EnhancedUnk23h = 0x28;
    private const uint EnhancedUnk44h = 2;
    private const byte EnhancedDim2D = 1;
    private const long EnhancedSrvVft = 0x00000001406B77D8;
    private const ushort EnhancedSrvDim2D = 0x41;
    private const int Rsc7VersionGen9 = 5;

    private byte[] BuildEnhanced()
    {
        var entries = _textures.OrderBy(t => Joaat(t.Name)).ToList();
        int n = entries.Count;
        if (n == 0)
            throw new InvalidOperationException("Cannot create ITD with zero textures");

        // Virtual layout (same dictionary header as legacy)
        int dictSize = 0x40;
        int keysOffset = dictSize;
        int ptrsOffset = Align(keysOffset + 4 * n, 16);
        int texturesOffset = Align(ptrsOffset + 8 * n, 16);

        int cur = texturesOffset + EnhancedTexSize * n;
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

        // Physical layout — gen9 uses align=1 (no block padding)
        var physOffsets = new List<int>();
        var physDataList = new List<byte[]>();
        int physCur = 0;
        foreach (var e in entries)
        {
            physOffsets.Add(physCur);
            int bc = Formats.BlockCount(e.Format, e.Width, e.Height, 1, e.MipCount, align: 1);
            int target = bc * Formats.BlockStride(e.Format);
            byte[] data = e.Data;
            if (data.Length < target)
            {
                byte[] padded = new byte[target];
                Array.Copy(data, padded, data.Length);
                data = padded;
            }
            physDataList.Add(data);
            physCur = Align(physCur + data.Length, 16);
        }
        int physicalSize = physCur;

        // Build virtual buffer
        byte[] vbuf = new byte[virtualSize];

        // Dictionary root (64 bytes)
        W64(vbuf, 0x00, 0); // VFT = 0
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

        // Hash array
        for (int i = 0; i < n; i++)
            W32(vbuf, keysOffset + 4 * i, Joaat(entries[i].Name));

        // Pointer array
        for (int i = 0; i < n; i++)
            W64(vbuf, ptrsOffset + 8 * i, Resource.VirtualBase + texturesOffset + EnhancedTexSize * i);

        // Texture blocks (128 bytes each)
        for (int i = 0; i < n; i++)
        {
            var e = entries[i];
            int off = texturesOffset + EnhancedTexSize * i;
            int bc = Formats.BlockCount(e.Format, e.Width, e.Height, 1, e.MipCount, align: 1);
            int bs = Formats.BlockStride(e.Format);

            // TextureBase (0x00–0x4F)
            W32(vbuf, off + 0x00, 0);   // VFT = 0
            W32(vbuf, off + 0x04, 1);   // Unknown_4h = 1
            W32(vbuf, off + 0x08, (uint)bc);
            W32(vbuf, off + 0x0C, (uint)bs);
            W32(vbuf, off + 0x10, EnhancedFlags);
            W32(vbuf, off + 0x14, 0);
            W16(vbuf, off + 0x18, (ushort)e.Width);
            W16(vbuf, off + 0x1A, (ushort)e.Height);
            W16(vbuf, off + 0x1C, 1); // depth
            vbuf[off + 0x1E] = EnhancedDim2D;
            vbuf[off + 0x1F] = Formats.ToRsc8(e.Format);
            vbuf[off + 0x20] = EnhancedTileAuto;
            vbuf[off + 0x21] = 0; // AntiAliasType
            vbuf[off + 0x22] = (byte)e.MipCount;
            vbuf[off + 0x23] = EnhancedUnk23h;
            vbuf[off + 0x24] = 0;
            vbuf[off + 0x25] = 0;
            W16(vbuf, off + 0x26, 1); // UsageCount
            W64(vbuf, off + 0x28, Resource.VirtualBase + nameOffsets[i]);
            W64(vbuf, off + 0x30, Resource.VirtualBase + off + 0x58); // SRV ptr
            W64(vbuf, off + 0x38, Resource.PhysicalBase + physOffsets[i]);
            W32(vbuf, off + 0x40, 0);
            W32(vbuf, off + 0x44, EnhancedUnk44h);
            W64(vbuf, off + 0x48, 0);

            // Texture extra (0x50–0x57)
            W64(vbuf, off + 0x50, 0);

            // Embedded ShaderResourceView (32 bytes at 0x58)
            W64(vbuf, off + 0x58, EnhancedSrvVft);
            W64(vbuf, off + 0x60, 0);
            W16(vbuf, off + 0x68, EnhancedSrvDim2D);
            W16(vbuf, off + 0x6A, 0xFFFF);
            W32(vbuf, off + 0x6C, 0xFFFFFFFF);
            W64(vbuf, off + 0x70, 0);
            W64(vbuf, off + 0x78, 0);
        }

        // Name strings
        for (int i = 0; i < nameBytesList.Count; i++)
            Array.Copy(nameBytesList[i], 0, vbuf, nameOffsets[i], nameBytesList[i].Length);

        // Pagemap
        vbuf[pagemapOffset] = 1;
        vbuf[pagemapOffset + 1] = 1;

        // Physical buffer
        byte[] pbuf = new byte[physicalSize];
        for (int i = 0; i < physDataList.Count; i++)
            Array.Copy(physDataList[i], 0, pbuf, physOffsets[i], physDataList[i].Length);

        return Resource.BuildRsc7(vbuf, pbuf, version: Rsc7VersionGen9);
    }

    private static ItdFile ParseEnhanced(byte[] fileData)
    {
        var (virtualData, physicalData) = Resource.DecompressRsc7(fileData);

        int count = R16(virtualData, 0x28);
        int itemsOff = V2O(R64(virtualData, 0x30));

        var itd = new ItdFile(Game.GtaVEnhanced);

        for (int i = 0; i < count; i++)
        {
            int texOff = V2O(R64(virtualData, itemsOff + 8 * i));

            string name = ReadName(virtualData, R64(virtualData, texOff + 0x28));
            int width = R16(virtualData, texOff + 0x18);
            int height = R16(virtualData, texOff + 0x1A);
            byte formatByte = virtualData[texOff + 0x1F];
            int mipLevels = virtualData[texOff + 0x22];
            long dataPtr = R64(virtualData, texOff + 0x38);

            BCFormat fmt = Formats.FromRsc8(formatByte);
            int physOff = P2O(dataPtr);
            int dataSize = Formats.TotalMipDataSize(width, height, fmt, mipLevels);
            byte[] pixelData = SliceTextureData(physicalData, physOff, dataSize,
                name, width, height, mipLevels);

            var (offsets, sizes) = BuildMipInfo(width, height, fmt, mipLevels);
            itd.Add(Texture.FromRaw(pixelData, width, height, fmt, mipLevels, offsets, sizes, name));
        }

        return itd;
    }

    private static List<TextureInfo> InspectEnhanced(byte[] fileData)
    {
        var (virtualData, _) = Resource.DecompressRsc7(fileData);

        int count = R16(virtualData, 0x28);
        int itemsOff = V2O(R64(virtualData, 0x30));

        var result = new List<TextureInfo>();
        for (int i = 0; i < count; i++)
        {
            int texOff = V2O(R64(virtualData, itemsOff + 8 * i));

            string name = ReadName(virtualData, R64(virtualData, texOff + 0x28));
            int width = R16(virtualData, texOff + 0x18);
            int height = R16(virtualData, texOff + 0x1A);
            byte formatByte = virtualData[texOff + 0x1F];
            int mipLevels = virtualData[texOff + 0x22];

            BCFormat? fmt = null;
            try { fmt = Formats.FromRsc8(formatByte); } catch { }

            string formatName = fmt.HasValue ? fmt.Value.ToString() : $"Unknown(0x{formatByte:X2})";
            int dataSize = fmt.HasValue
                ? Formats.TotalMipDataSize(width, height, fmt.Value, mipLevels) : 0;

            result.Add(new TextureInfo(name, width, height,
                fmt ?? BCFormat.BC7, mipLevels, dataSize));
        }

        return result;
    }

    // ═════════════════════════════════════════════════════════════════════
    // GTA IV (RSC5 / .wtd)
    // ═════════════════════════════════════════════════════════════════════

    private const int Gta4TexSize = 80;
    private const int Gta4DictSize = 32;
    private const int Gta4BlockMapSize = 528;
    private const int V32Base = 0x50000000;
    private const int P32Base = 0x60000000;

    private static int V2O32(uint addr) => (int)addr - V32Base;
    private static int P2O32(uint addr) => (int)addr - P32Base;

    private static string ReadNameGta4(byte[] virtualData, uint namePtr)
    {
        int off = V2O32(namePtr);
        int end = Array.IndexOf(virtualData, (byte)0, off);
        string raw = Encoding.UTF8.GetString(virtualData, off, end - off);
        if (raw.StartsWith("pack:/", StringComparison.OrdinalIgnoreCase))
            raw = raw[6..];
        if (raw.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            raw = raw[..^4];
        return raw;
    }

    private byte[] BuildGta4()
    {
        var entries = _textures.OrderBy(t => Joaat(t.Name)).ToList();
        int n = entries.Count;
        if (n == 0)
            throw new InvalidOperationException("Cannot create ITD with zero textures");

        foreach (var e in entries)
        {
            if (!Formats.IsGta4Supported(e.Format))
                throw new InvalidOperationException(
                    $"Format {e.Format} is not supported by GTA IV. Convert to BC1, BC2, BC3, A8R8G8B8, B5G5R5A1, B5G6R5, A8, or R8 first.");
        }

        int blockmapOff = Gta4DictSize;
        int hashOff = Align(blockmapOff + Gta4BlockMapSize, 16);
        int ptrOff = Align(hashOff + n * 4, 16);
        int texOffBase = Align(ptrOff + n * 4, 16);

        int cur = texOffBase + Gta4TexSize * n;
        var nameOffsets = new List<int>();
        var nameBytesList = new List<byte[]>();
        foreach (var e in entries)
        {
            nameOffsets.Add(cur);
            byte[] encoded = Encoding.UTF8.GetBytes($"pack:/{e.Name}.dds\0");
            nameBytesList.Add(encoded);
            cur += encoded.Length;
        }

        int virtualSize = Align(cur, 16);

        var physOffsets = new List<int>();
        int physCur = 0;
        foreach (var e in entries)
        {
            physOffsets.Add(physCur);
            physCur += e.Data.Length;
        }

        byte[] vbuf = new byte[virtualSize];

        W32(vbuf, 0x00, 0);
        W32(vbuf, 0x04, (uint)(V32Base + blockmapOff));
        W32(vbuf, 0x08, 0);
        W32(vbuf, 0x0C, 1);
        W32(vbuf, 0x10, (uint)(V32Base + hashOff));
        W16(vbuf, 0x14, (ushort)n);
        W16(vbuf, 0x16, (ushort)n);
        W32(vbuf, 0x18, (uint)(V32Base + ptrOff));
        W16(vbuf, 0x1C, (ushort)n);
        W16(vbuf, 0x1E, (ushort)n);

        W32(vbuf, blockmapOff, 0);
        for (int i = 1; i < 132; i++)
            W32(vbuf, blockmapOff + i * 4, 0xCDCDCDCD);

        for (int i = 0; i < n; i++)
            W32(vbuf, hashOff + 4 * i, Joaat(entries[i].Name));

        for (int i = 0; i < n; i++)
            W32(vbuf, ptrOff + 4 * i, (uint)(V32Base + texOffBase + Gta4TexSize * i));

        for (int i = 0; i < n; i++)
        {
            var e = entries[i];
            int off = texOffBase + Gta4TexSize * i;
            uint formatVal = Formats.ToRsc5(e.Format);
            int stride = e.Width * Formats.BitsPerPixel(e.Format) / 8;

            W32(vbuf, off + 0x00, 0);
            W32(vbuf, off + 0x04, 0);
            W16(vbuf, off + 0x08, 1);
            W16(vbuf, off + 0x0A, 0);
            W32(vbuf, off + 0x0C, 0);
            W32(vbuf, off + 0x10, 0);
            W32(vbuf, off + 0x14, (uint)(V32Base + nameOffsets[i]));
            W32(vbuf, off + 0x18, 0);

            W16(vbuf, off + 0x1C, (ushort)e.Width);
            W16(vbuf, off + 0x1E, (ushort)e.Height);
            W32(vbuf, off + 0x20, formatVal);
            W16(vbuf, off + 0x24, (ushort)stride);
            vbuf[off + 0x26] = 0;
            vbuf[off + 0x27] = (byte)e.MipCount;
            WriteFloat(vbuf, off + 0x28, 1.0f);
            WriteFloat(vbuf, off + 0x2C, 1.0f);
            WriteFloat(vbuf, off + 0x30, 1.0f);
            WriteFloat(vbuf, off + 0x34, 0.0f);
            WriteFloat(vbuf, off + 0x38, 0.0f);
            WriteFloat(vbuf, off + 0x3C, 0.0f);
            W32(vbuf, off + 0x40, 0);
            W32(vbuf, off + 0x44, 0);
            W32(vbuf, off + 0x48, (uint)(P32Base + physOffsets[i]));
            W32(vbuf, off + 0x4C, 0);
        }

        for (int i = 0; i < nameBytesList.Count; i++)
            Array.Copy(nameBytesList[i], 0, vbuf, nameOffsets[i], nameBytesList[i].Length);

        byte[] pbuf = new byte[physCur];
        for (int i = 0; i < entries.Count; i++)
            Array.Copy(entries[i].Data, 0, pbuf, physOffsets[i], entries[i].Data.Length);

        return Rsc5.BuildRsc5(vbuf, pbuf);
    }

    private static ItdFile ParseGta4(byte[] fileData)
    {
        var (virtualData, physicalData) = Rsc5.DecompressRsc5(fileData);

        int count = R16(virtualData, 0x14);
        int ptrArrOff = V2O32(R32(virtualData, 0x18));

        var itd = new ItdFile(Game.GtaIV);

        for (int i = 0; i < count; i++)
        {
            int texOff = V2O32(R32(virtualData, ptrArrOff + 4 * i));

            string name = ReadNameGta4(virtualData, R32(virtualData, texOff + 0x14));
            int width = R16(virtualData, texOff + 0x1C);
            int height = R16(virtualData, texOff + 0x1E);
            uint formatVal = R32(virtualData, texOff + 0x20);
            int mipLevels = virtualData[texOff + 0x27];
            uint dataPtr = R32(virtualData, texOff + 0x48);

            BCFormat fmt = Formats.FromRsc5(formatVal);
            int physOff = P2O32(dataPtr);
            int dataSize = Formats.TotalMipDataSize(width, height, fmt, mipLevels);
            byte[] pixelData = SliceTextureData(physicalData, physOff, dataSize,
                name, width, height, mipLevels);

            var (offsets, sizes) = BuildMipInfo(width, height, fmt, mipLevels);
            itd.Add(Texture.FromRaw(pixelData, width, height, fmt, mipLevels, offsets, sizes, name));
        }

        return itd;
    }

    private static List<TextureInfo> InspectGta4(byte[] fileData)
    {
        var (virtualData, _) = Rsc5.DecompressRsc5(fileData);

        int count = R16(virtualData, 0x14);
        int ptrArrOff = V2O32(R32(virtualData, 0x18));

        var result = new List<TextureInfo>();
        for (int i = 0; i < count; i++)
        {
            int texOff = V2O32(R32(virtualData, ptrArrOff + 4 * i));

            string name = ReadNameGta4(virtualData, R32(virtualData, texOff + 0x14));
            int width = R16(virtualData, texOff + 0x1C);
            int height = R16(virtualData, texOff + 0x1E);
            uint formatVal = R32(virtualData, texOff + 0x20);
            int mipLevels = virtualData[texOff + 0x27];

            BCFormat? fmt = null;
            try { fmt = Formats.FromRsc5(formatVal); } catch { }

            int dataSize = fmt.HasValue
                ? Formats.TotalMipDataSize(width, height, fmt.Value, mipLevels)
                : 0;

            result.Add(new TextureInfo(name, width, height,
                fmt ?? BCFormat.BC1, mipLevels, dataSize));
        }

        return result;
    }

    private static void WriteFloat(byte[] data, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), BitConverter.SingleToInt32Bits(value));

    // ── High-level convenience methods ───────────────────────────────────

    /// <summary>Build an ITD from all images/DDS files in a folder without writing it.</summary>
    public static ItdFile FromFolder(string folder,
        Game game = Game.GtaVLegacy,
        BCFormat format = BCFormat.BC7, float quality = 0.7f,
        bool generateMipmaps = true, int minMipSize = 4,
        MipFilter mipFilter = MipFilter.Mitchell,
        Action<int, int, string>? onProgress = null)
    {
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Not a directory: {folder}");

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

        return itd;
    }

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
                                Path.GetFileName(folder) + (game == Game.GtaIV ? ".wtd" : ".ytd"));

        var itd = FromFolder(folder, game, format, quality, generateMipmaps, minMipSize, mipFilter, onProgress);
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

    /// <summary>Extract all textures from this ITD as DDS files.</summary>
    public string ExtractTo(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        foreach (var tex in _textures)
            tex.SaveDds(Path.Combine(outputDir, tex.Name + ".dds"));
        return outputDir;
    }
}
