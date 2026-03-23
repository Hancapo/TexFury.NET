namespace TexFury;

/// <summary>Ensures the native DLL can be found at runtime.</summary>
internal static class NativeResolver
{
    private static int _initialized;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0) return;

        // Add the runtimes directory to the DLL search path via PATH
        var assembly = typeof(NativeResolver).Assembly;
        string assemblyDir = Path.GetDirectoryName(assembly.Location) ?? "";
        string nativeDir = Path.Combine(assemblyDir, "runtimes", "win-x64", "native");

        if (Directory.Exists(nativeDir))
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            Environment.SetEnvironmentVariable("PATH", nativeDir + ";" + path);
        }
    }
}
