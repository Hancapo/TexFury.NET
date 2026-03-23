using System.Reflection;
using System.Runtime.InteropServices;

namespace TexFury;

internal static class NativeResolver
{
    private static int _initialized;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0) return;
        NativeLibrary.SetDllImportResolver(typeof(NativeResolver).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != "texfury_native")
            return IntPtr.Zero;

        // Try default resolution first
        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out IntPtr handle))
            return handle;

        // Try runtimes/win-x64/native/ next to the assembly
        string assemblyDir = Path.GetDirectoryName(assembly.Location)!;
        string runtimePath = Path.Combine(assemblyDir, "runtimes", "win-x64", "native", "texfury_native.dll");
        if (NativeLibrary.TryLoad(runtimePath, out handle))
            return handle;

        return IntPtr.Zero;
    }
}
