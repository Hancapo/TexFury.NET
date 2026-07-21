using System.Runtime.InteropServices;

namespace TexFury;

internal static class Oodle
{
    private const int OodleFormatKraken = 8;
    private const int OodleLevelNormal = 4;

    private static readonly string[] DllNames =
    [
        "oo2core_5_win64",
        "oo2core_9_win64",
        "oo2core_8_win64",
        "oo2core_7_win64",
        "oo2core_6_win64",
    ];

    private delegate long OodleLzCompressDelegate(
        int compressor,
        IntPtr rawBuf,
        long rawLen,
        IntPtr compBuf,
        int level,
        long opts,
        long dictionaryBase,
        long lrm);

    private delegate long OodleLzDecompressDelegate(
        IntPtr compBuf,
        long compBufSize,
        IntPtr rawBuf,
        long rawLen,
        int fuzzSafe,
        int checkCrc,
        int verbosity,
        long decBufBase,
        long decBufSize,
        long fpCallback,
        long callbackUserData,
        long decoderMemory,
        long decoderMemorySize,
        int threadPhase);

    private static IntPtr _library;
    private static OodleLzCompressDelegate? _compress;
    private static OodleLzDecompressDelegate? _decompress;

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryW(string lpFileName);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    public static bool IsAvailable => EnsureLoaded(throwOnFailure: false);

    public static byte[] Compress(byte[] data)
    {
        EnsureLoaded();
        int maxSize = data.Length + (data.Length / 4) + 0x10000;
        byte[] output = new byte[maxSize];

        unsafe
        {
            fixed (byte* src = data)
            fixed (byte* dst = output)
            {
                long size = _compress!(OodleFormatKraken, (IntPtr)src, data.Length,
                    (IntPtr)dst, OodleLevelNormal, 0, 0, 0);
                if (size <= 0)
                    throw new InvalidOperationException("Oodle compression failed");
                Array.Resize(ref output, (int)size);
                return output;
            }
        }
    }

    public static byte[] Decompress(byte[] data, int decompressedSize)
    {
        EnsureLoaded();
        byte[] output = new byte[decompressedSize];

        unsafe
        {
            fixed (byte* src = data)
            fixed (byte* dst = output)
            {
                long size = _decompress!((IntPtr)src, data.Length, (IntPtr)dst,
                    decompressedSize, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                if (size != decompressedSize)
                    throw new InvalidOperationException(
                        $"Oodle decompression failed: expected {decompressedSize} bytes, got {size}");
                return output;
            }
        }
    }

    private static void EnsureLoaded() => EnsureLoaded(throwOnFailure: true);

    private static bool EnsureLoaded(bool throwOnFailure)
    {
        if (_compress is not null && _decompress is not null)
            return true;

        foreach (string name in DllNames)
        {
            _library = LoadLibraryW(name + ".dll");
            if (_library == IntPtr.Zero)
                continue;

            IntPtr compressPtr = GetProcAddress(_library, "OodleLZ_Compress");
            IntPtr decompressPtr = GetProcAddress(_library, "OodleLZ_Decompress");
            if (compressPtr != IntPtr.Zero && decompressPtr != IntPtr.Zero)
            {
                _compress = Marshal.GetDelegateForFunctionPointer<OodleLzCompressDelegate>(compressPtr);
                _decompress = Marshal.GetDelegateForFunctionPointer<OodleLzDecompressDelegate>(decompressPtr);
                return true;
            }
        }

        string[] searchDirs =
        [
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native"),
            Path.Combine(Path.GetDirectoryName(typeof(Oodle).Assembly.Location) ?? "", "runtimes", "win-x64", "native"),
        ];

        foreach (string dir in searchDirs)
        foreach (string name in DllNames)
        {
            string path = Path.Combine(dir, name + ".dll");
            if (!File.Exists(path))
                continue;

            _library = LoadLibraryW(path);
            if (_library == IntPtr.Zero)
                continue;

            IntPtr compressPtr = GetProcAddress(_library, "OodleLZ_Compress");
            IntPtr decompressPtr = GetProcAddress(_library, "OodleLZ_Decompress");
            if (compressPtr != IntPtr.Zero && decompressPtr != IntPtr.Zero)
            {
                _compress = Marshal.GetDelegateForFunctionPointer<OodleLzCompressDelegate>(compressPtr);
                _decompress = Marshal.GetDelegateForFunctionPointer<OodleLzDecompressDelegate>(decompressPtr);
                return true;
            }
        }

        if (throwOnFailure)
            throw new InvalidOperationException(
                "Oodle library not found. Place oo2core_5_win64.dll or a compatible oo2core DLL next to the application or in PATH.");
        return false;
    }
}
