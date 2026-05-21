using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace MyEfVibe;

/// <summary>
/// Loads <c>e_sqlite3</c> from the workspace <c>.deps.json</c> RID native assets (e.g.
/// <c>runtimes/osx-x64/native/libe_sqlite3.dylib</c>). Library outputs often omit these files;
/// without this, Microsoft.Data.Sqlite.SqliteConnection fails in the efvibe host process.
/// </summary>
internal static class WorkspaceSqliteNativeBootstrap
{
    private static readonly object Gate = new();
    private static string? _nativeLibraryPath;
    private static IntPtr _nativeHandle;

    internal static void EnsureRegistered(WorkspaceDepsManifest? depsManifest)
    {
        if (depsManifest is null)
            return;

        if (!depsManifest.TryResolveNativeLibrary(out var nativePath, GetCandidateNativeFileNames()))
            return;

        lock (Gate)
        {
            if (_nativeLibraryPath is not null)
                return;

            _nativeLibraryPath = nativePath;
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += OnResolvingUnmanagedDll;

            try
            {
                _nativeHandle = NativeLibrary.Load(nativePath);
            }
            catch (DllNotFoundException)
            {
            }
            catch (BadImageFormatException)
            {
            }
        }
    }

    private static IntPtr OnResolvingUnmanagedDll(Assembly assembly, string unmanagedDllName)
    {
        if (_nativeLibraryPath is null || !IsSqliteNativeName(unmanagedDllName))
            return IntPtr.Zero;

        if (_nativeHandle != IntPtr.Zero)
            return _nativeHandle;

        try
        {
            _nativeHandle = NativeLibrary.Load(_nativeLibraryPath);
            return _nativeHandle;
        }
        catch (DllNotFoundException)
        {
            return IntPtr.Zero;
        }
        catch (BadImageFormatException)
        {
            return IntPtr.Zero;
        }
    }

    private static bool IsSqliteNativeName(string unmanagedDllName)
        => unmanagedDllName.Equals("e_sqlite3", StringComparison.OrdinalIgnoreCase)
           || unmanagedDllName.Equals("libe_sqlite3", StringComparison.OrdinalIgnoreCase)
           || unmanagedDllName.Contains("e_sqlite3", StringComparison.OrdinalIgnoreCase);

    private static string[] GetCandidateNativeFileNames()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return
            [
                "libe_sqlite3.dylib",
                "e_sqlite3.dylib",
            ];
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return
            [
                "libe_sqlite3.so",
                "e_sqlite3.so",
            ];
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ["e_sqlite3.dll"];

        return ["e_sqlite3.dll", "libe_sqlite3.so", "libe_sqlite3.dylib"];
    }
}
