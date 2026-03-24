namespace TexFury;

/// <summary>Lightweight metadata about a texture (no pixel data loaded).</summary>
public sealed class TextureInfo
{
    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    public BCFormat Format { get; }
    public int MipCount { get; }
    public int DataSize { get; }

    public TextureInfo(string name, int width, int height, BCFormat format, int mipCount, int dataSize)
    {
        Name = name;
        Width = width;
        Height = height;
        Format = format;
        MipCount = mipCount;
        DataSize = dataSize;
    }

    public override string ToString() =>
        $"TextureInfo(Name={Name}, {Width}x{Height}, Format={Format}, Mips={MipCount}, Size={DataSize})";
}
