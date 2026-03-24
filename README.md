# TexFury.NET

Fast image-to-DDS conversion and YTD texture dictionary toolkit for .NET.

Built on **bc7enc_rdo** + **ISPC bc7e** for high-quality BC1/BC3/BC4/BC5/BC7 compression, with support for uncompressed A8R8G8B8 textures. No DirectXTex dependency — a single native DLL handles everything.

> This is the .NET port of the [Python texfury](https://github.com/Hancapo/texfury) package. Identical functionality, same native backend.

## Features

- **BC1, BC3, BC4, BC5, BC7** block compression with adjustable quality (0.0–1.0)
- **A8R8G8B8** uncompressed 32-bit BGRA format
- **DDS** file read/write (legacy + DX10 extended headers)
- **YTD** texture dictionary creation, extraction, and editing
- **Decompression** — decompress any texture back to RGBA pixels
- **Format suggestion** — auto-detect the best BCFormat for your image
- **Quality metrics** — PSNR (RGB/RGBA) and SSIM comparison
- **Texture validation** — detect common issues (non-POT, size mismatch, etc.)
- **DDS/YTD inspection** — read metadata without loading pixel data
- **Mipmap generation** with configurable minimum size and 6 downsampling filters
- **Automatic power-of-two resize** (sRGB-aware via stb_image_resize2)
- **Transparency detection** without manual pixel iteration
- **Batch operations** with progress callbacks
- **Zero dependencies** — targets .NET Standard 2.1

## Installation

Add the `TexFury` project reference or NuGet package. The pre-compiled `texfury_native.dll` is bundled and copied to output automatically.

> **Target:** .NET Standard 2.1 — compatible with .NET Core 3.0+, .NET 5/6/7/8+, Unity 2021+, and Mono.

---

## Quick Start

### Convert a single image to DDS

```csharp
using TexFury;

var tex = Texture.FromImage("logo.png", format: BCFormat.BC7, quality: 0.8f);
tex.SaveDds("logo.dds");
```

### Create a YTD from a folder of images

```csharp
using TexFury;

YtdFile.CreateFromFolder(
    "my_textures/",
    "output.ytd",
    format: BCFormat.BC3,
    quality: 0.7f
);
```

### Extract textures from a YTD

```csharp
using TexFury;

YtdFile.ExtractYtd("vehicles.ytd", "extracted/");
// Creates extracted/texture_name.dds for each texture
```

---

## API Reference

### `BCFormat` — Compression Formats

```csharp
using TexFury;
```

| Value | Name | Description |
|-------|------|-------------|
| `BCFormat.BC1` | DXT1 | RGB, 6:1 ratio. No alpha. Smallest files. |
| `BCFormat.BC3` | DXT5 | RGBA, 4:1 ratio. Full alpha channel. |
| `BCFormat.BC4` | ATI1 | Single channel (R), 4:1 ratio. Grayscale/height maps. |
| `BCFormat.BC5` | ATI2 | Two channels (RG), 4:1 ratio. Normal maps. |
| `BCFormat.BC7` | BC7 | RGBA, 4:1 ratio. Best quality, slowest to encode. |
| `BCFormat.A8R8G8B8` | Uncompressed | 32-bit BGRA. No compression, largest files. |

**Choosing a format:**

- **Opaque textures** (no transparency): `BC1` for speed/size, `BC7` for quality
- **Textures with alpha**: `BC3` or `BC7`
- **Normal maps**: `BC5`
- **Grayscale / height maps**: `BC4`
- **Must be pixel-perfect**: `A8R8G8B8`

---

### `MipFilter` — Downsampling Filters

Controls how pixels are interpolated when generating mipmaps and resizing to power-of-two.

```csharp
using TexFury;
```

| Value | Description | Best for |
|-------|-------------|----------|
| `MipFilter.Mitchell` | Balanced sharpness/smoothness (B=1/3, C=1/3). **Default.** | General-purpose |
| `MipFilter.Box` | Simple pixel average. Fast, correct for exact 2:1 downscale. | Fast iteration |
| `MipFilter.Triangle` | Bilinear interpolation. | Smooth gradients |
| `MipFilter.CatmullRom` | Sharp cubic interpolation. | Preserving edges/detail |
| `MipFilter.CubicBSpline` | Gaussian-like smoothing (B=1, C=0). | Maximum smoothness |
| `MipFilter.Point` | Nearest-neighbor, no interpolation. | Pixel art |

---

### `Texture` — Core Texture Object

Every operation in TexFury produces or consumes a `Texture` object.

#### Properties

```csharp
tex.Width      // int — pixel width
tex.Height     // int — pixel height
tex.Format     // BCFormat — compression format
tex.MipCount   // int — number of mipmap levels
tex.Name       // string — texture name (read/write)
tex.Data       // byte[] — raw pixel data (all mip levels concatenated)
```

#### Creating Textures

##### `Texture.FromImage(path, format, quality, generateMipmaps, minMipSize, resizeToPot, mipFilter, name)`

Load an image file and compress it.

```csharp
var tex = Texture.FromImage(
    "photo.png",
    format: BCFormat.BC7,            // default
    quality: 0.7f,                   // 0.0 = fastest, 1.0 = best quality
    generateMipmaps: true,           // default
    minMipSize: 4,                   // smallest mip dimension (default: 4)
    resizeToPot: true,               // auto-resize to power-of-two (default)
    mipFilter: MipFilter.Mitchell,   // downsampling filter (default)
    name: "my_texture"               // defaults to filename stem
);
```

**Supported image formats:** PNG, JPG/JPEG, TGA, BMP, PSD, WebP, GIF, HDR, PNM/PPM.

##### `Texture.FromPixels(rgbaData, width, height, format, quality, generateMipmaps, minMipSize, resizeToPot, mipFilter, name)`

Create from raw RGBA pixel data in memory.

```csharp
byte[] pixels = GetPixelsFromSomewhere();
var tex = Texture.FromPixels(pixels, 512, 512, format: BCFormat.BC3, quality: 0.9f);
tex.SaveDds("result.dds");
```

##### `Texture.FromDds(path, name)`

Load an existing DDS file.

```csharp
var tex = Texture.FromDds("existing.dds");
Console.WriteLine($"{tex.Format} {tex.Width}x{tex.Height} ({tex.MipCount} mips)");
```

##### `Texture.FromRaw(data, width, height, format, mipCount, mipOffsets, mipSizes, name)`

Create from raw compressed pixel data (advanced / internal use).

```csharp
var tex = Texture.FromRaw(
    data: rawBytes,
    width: 256, height: 256,
    format: BCFormat.BC7,
    mipCount: 7,
    mipOffsets: new[] { 0, 65536, ... },
    mipSizes: new[] { 65536, 16384, ... },
    name: "custom"
);
```

#### Decompression & Analysis

##### `tex.ToRgba(mip)`

Decompress to raw RGBA pixels. Returns `(byte[] Rgba, int Width, int Height)`.

```csharp
var (rgba, w, h) = tex.ToRgba();    // mip 0
var (rgba1, w1, h1) = tex.ToRgba(1); // mip 1
```

##### `tex.QualityMetrics(originalRgba)`

Compare compressed texture against original RGBA pixels. Returns a `QualityMetrics` object with `PsnrRgb`, `PsnrRgba`, and `Ssim` properties.

##### `tex.Validate()`

Check for common issues. Returns `List<string>` — empty means OK.

##### `Texture.InspectDds(path)`

Read DDS metadata without loading pixel data. Returns a `TextureInfo` object.

#### Format Suggestion

##### `Formats.SuggestFormat(hasAlpha, normalMap, singleChannel, qualityOverSize)`

Returns the recommended `BCFormat` based on image characteristics.

```csharp
var fmt = Formats.SuggestFormat(hasAlpha: true);  // BC7
```

#### Saving Textures

##### `tex.SaveDds(path)`

Write to a DDS file.

```csharp
tex.SaveDds("output.dds");
```

##### `tex.ToDdsBytes()`

Get the complete DDS file as a `byte[]` (useful for in-memory pipelines).

```csharp
byte[] ddsData = tex.ToDdsBytes();
File.WriteAllBytes("output.dds", ddsData);
```

---

### `YtdFile` — Texture Dictionary (.ytd)

#### Building a YTD

```csharp
using TexFury;

var ytd = new YtdFile();
ytd.Add(Texture.FromImage("diffuse.png", format: BCFormat.BC7));
ytd.Add(Texture.FromImage("normal.png", format: BCFormat.BC5));
ytd.Add(Texture.FromImage("specular.png", format: BCFormat.BC1));
ytd.Save("my_vehicle.ytd");

Console.WriteLine(ytd.Count); // 3
```

#### Loading and Inspecting a YTD

```csharp
var ytd = YtdFile.Load("vehicles.ytd");

foreach (var tex in ytd.Textures)
    Console.WriteLine($"{tex.Name}: {tex.Width}x{tex.Height} {tex.Format} ({tex.MipCount} mips)");
```

#### Editing Textures

```csharp
var ytd = YtdFile.Load("vehicles.ytd");

ytd.Names();                  // List<string> of texture names
ytd.Contains("body_d");       // bool
ytd.Get("body_d");            // Texture (throws KeyNotFoundException if missing)
ytd.Replace("body_d", newTex); // replace in-place, keeps original name
ytd.Remove("old_texture");    // returns true if removed, false if not found
```

#### Inspecting a YTD

```csharp
var entries = YtdFile.Inspect("vehicles.ytd");
// Returns List<TextureInfo> — metadata only, no pixel data loaded
```

#### Extracting to DDS

```csharp
var ytd = YtdFile.Load("props.ytd");
foreach (var tex in ytd.Textures)
    tex.SaveDds($"extracted/{tex.Name}.dds");
```

**Important:** Texture names must be set before adding to a YTD. Names are automatically set from filenames when using `FromImage()` or `FromDds()`.

---

### Convenience Methods

#### `YtdFile.CreateFromFolder(folder, output, format, quality, generateMipmaps, minMipSize, mipFilter, onProgress)`

Convert all images in a folder into a single YTD file. Also picks up `.dds` files.

```csharp
using TexFury;

string path = YtdFile.CreateFromFolder(
    "textures/",
    "output.ytd",
    format: BCFormat.BC7,
    quality: 0.8f,
    onProgress: (i, total, name) => Console.WriteLine($"[{i}/{total}] {name}")
);
Console.WriteLine($"Created: {path}");
```

| Parameter | Default | Description |
|-----------|---------|-------------|
| `folder` | — | Directory with image files |
| `output` | `<folder>.ytd` | Output path |
| `format` | `BC7` | Compression format for all textures |
| `quality` | `0.7f` | Compression quality 0.0–1.0 |
| `generateMipmaps` | `true` | Generate mipmap chain |
| `minMipSize` | `4` | Minimum mip dimension |
| `mipFilter` | `Mitchell` | Downsampling filter for mipmaps |
| `onProgress` | `null` | Callback `Action<int, int, string>` |

#### `YtdFile.BatchConvert(folder, outputDir, format, quality, generateMipmaps, minMipSize, mipFilter, onProgress)`

Convert all images in a folder to individual DDS files.

```csharp
using TexFury;

string outDir = YtdFile.BatchConvert(
    "raw_textures/",
    "dds_output/",
    format: BCFormat.BC3,
    quality: 0.6f,
    onProgress: (i, total, name) => Console.WriteLine($"[{i}/{total}] {name}")
);
```

Parameters are the same as `CreateFromFolder`, except `outputDir` defaults to `<folder>/dds_out/`.

#### `YtdFile.ExtractYtd(ytdPath, outputDir)`

Extract all textures from a YTD into DDS files.

```csharp
using TexFury;

string output = YtdFile.ExtractYtd("vehicles.ytd");
// Creates vehicles/texture1.dds, vehicles/texture2.dds, ...

string output2 = YtdFile.ExtractYtd("vehicles.ytd", "my_folder/");
// Extracts into my_folder/
```

---

### Image Utilities

Standalone helper methods that work without compressing anything.

#### `ImageUtils.HasTransparency(path)`

Check if an image file has transparent pixels.

```csharp
using TexFury;

if (ImageUtils.HasTransparency("icon.png"))
    Console.WriteLine("Has transparency — use BC3 or BC7");
else
    Console.WriteLine("Fully opaque — BC1 is fine");
```

#### `ImageUtils.IsPowerOfTwo(width, height)`

Check if both dimensions are powers of two.

```csharp
ImageUtils.IsPowerOfTwo(256, 512);  // true
ImageUtils.IsPowerOfTwo(300, 400);  // false
```

#### `ImageUtils.NextPowerOfTwo(value)`

Get the nearest power-of-two >= the given value.

```csharp
ImageUtils.NextPowerOfTwo(100);  // 128
ImageUtils.NextPowerOfTwo(256);  // 256
ImageUtils.NextPowerOfTwo(500);  // 512
```

#### `ImageUtils.PotDimensions(width, height)`

Get power-of-two dimensions for a given size.

```csharp
var (w, h) = ImageUtils.PotDimensions(300, 400);   // (512, 512)
var (w2, h2) = ImageUtils.PotDimensions(1920, 1080); // (2048, 2048)
```

#### `ImageUtils.ImageDimensions(path)`

Get width, height, and channel count of an image.

```csharp
var (w, h, ch) = ImageUtils.ImageDimensions("photo.png");
Console.WriteLine($"{w}x{h}, {ch} channels"); // e.g. 1920x1080, 4 channels
```

---

## Examples

### Auto-detect format based on transparency

```csharp
using TexFury;

Texture SmartCompress(string path, float quality = 0.8f)
{
    var fmt = ImageUtils.HasTransparency(path) ? BCFormat.BC3 : BCFormat.BC1;
    return Texture.FromImage(path, format: fmt, quality: quality);
}

var tex = SmartCompress("my_texture.png");
tex.SaveDds("my_texture.dds");
```

### Build a YTD with mixed formats

```csharp
using TexFury;

var ytd = new YtdFile();

// Opaque diffuse — BC1 is fine, smallest size
ytd.Add(Texture.FromImage("body_d.png", format: BCFormat.BC1, quality: 0.7f));

// Normal map — BC5 stores RG channels
ytd.Add(Texture.FromImage("body_n.png", format: BCFormat.BC5, quality: 0.8f));

// Specular with transparency — BC3
ytd.Add(Texture.FromImage("body_s.png", format: BCFormat.BC3, quality: 0.7f));

// Emissive — uncompressed for precision
ytd.Add(Texture.FromImage("body_e.png", format: BCFormat.A8R8G8B8));

ytd.Save("body.ytd");
```

### Decompress a texture to RGBA pixels

```csharp
using TexFury;

var tex = Texture.FromDds("diffuse.dds");
var (rgba, w, h) = tex.ToRgba();       // mip 0 (full resolution)
var (rgba1, w1, h1) = tex.ToRgba(1);   // mip 1 (half resolution)
```

### Suggest the best format for an image

```csharp
using TexFury;

var fmt = Formats.SuggestFormat(hasAlpha: true);                       // BC7
var fmt2 = Formats.SuggestFormat(hasAlpha: true, qualityOverSize: false); // BC3
var fmt3 = Formats.SuggestFormat(hasAlpha: false, normalMap: true);    // BC5
var fmt4 = Formats.SuggestFormat(hasAlpha: false, singleChannel: true); // BC4

// Combined with transparency detection:
var format = Formats.SuggestFormat(ImageUtils.HasTransparency("icon.png"));
var tex = Texture.FromImage("icon.png", format: format);
```

### Measure compression quality

```csharp
using TexFury;

// Load raw pixels, compress, then compare
byte[] originalRgba = GetOriginalPixels();
var tex = Texture.FromPixels(originalRgba, 512, 512, format: BCFormat.BC7);

var metrics = tex.QualityMetrics(originalRgba);
Console.WriteLine($"PSNR RGB:  {metrics.PsnrRgb:F2} dB");
Console.WriteLine($"PSNR RGBA: {metrics.PsnrRgba:F2} dB");
Console.WriteLine($"SSIM:      {metrics.Ssim:F4}");
```

### Validate a texture

```csharp
using TexFury;

var tex = Texture.FromDds("suspect.dds");
var warnings = tex.Validate();

if (warnings.Count == 0)
    Console.WriteLine("Texture is OK");
else
    foreach (var w in warnings)
        Console.WriteLine($"Warning: {w}");
```

Checks for: invalid dimensions, non-POT, below-minimum BC size, missing mips, data size mismatch, oversized textures, missing name.

### Inspect DDS/YTD metadata without loading pixel data

```csharp
using TexFury;

// Inspect a single DDS file
var info = Texture.InspectDds("large_texture.dds");
Console.WriteLine($"{info.Name}: {info.Width}x{info.Height} {info.Format} ({info.MipCount} mips, {info.DataSize} bytes)");

// Inspect all textures inside a YTD
var entries = YtdFile.Inspect("vehicles.ytd");
foreach (var entry in entries)
    Console.WriteLine($"{entry.Name}: {entry.Width}x{entry.Height} {entry.Format}");
```

### Edit a YTD — replace, remove, query textures

```csharp
using TexFury;

var ytd = YtdFile.Load("vehicles.ytd");

// Query
var names = ytd.Names();                     // List<string>
bool has = ytd.Contains("body_d");           // true/false
var tex = ytd.Get("body_d");                 // Texture (throws if not found)

// Replace a single texture
var newTex = Texture.FromImage("body_d_new.png", format: BCFormat.BC7);
ytd.Replace("body_d", newTex);               // keeps original name

// Remove a texture
ytd.Remove("old_unused_texture");

ytd.Save("vehicles_modified.ytd");
```

### Re-pack an existing YTD with different compression

```csharp
using TexFury;

// Extract original
YtdFile.ExtractYtd("original.ytd", "temp_textures/");

// Re-pack with BC7 (original may have used DXT1/DXT5)
YtdFile.CreateFromFolder("temp_textures/", "repacked.ytd",
    format: BCFormat.BC7, quality: 0.9f);
```

---

## Quality Guide

The `quality` parameter (0.0–1.0) maps to the encoder's internal quality levels:

| Range | Speed | Quality | Use case |
|-------|-------|---------|----------|
| 0.0–0.2 | Fastest | Low | Quick previews, testing |
| 0.3–0.5 | Fast | Medium | Development builds |
| 0.6–0.8 | Moderate | High | Production use (recommended) |
| 0.9–1.0 | Slow | Maximum | Final release, archival |

BC7 is the slowest format to encode but produces the best visual quality. For rapid iteration, use `BC1` or `BC3` at lower quality, then do a final pass with `BC7` at 0.8+.

---

## Limitations

- **Windows only** — the native DLL is compiled for x64 Windows with MSVC
- **Power-of-two textures** — YTD requires POT dimensions; `resizeToPot: true` handles this automatically
- **No BC2 / BC6H** — BC2 (DXT3) is rarely used; BC6H (HDR) may be added later
- **Max texture size** — limited by available memory; typical textures are 256–2048px
