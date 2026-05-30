using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace MyEfVibe;

/// <summary>
///     Loads <c>e_sqlite3</c> from the workspace <c>.deps.json</c> RID native assets (e.g.
///     <c>runtimes/osx-x64/native/libe_sqlite3.dylib</c>). Library outputs often omit these files;
///     without this, Microsoft.Data.Sqlite.SqliteConnection fails in the efvibe host process.
/// </summary>
internal static class WorkspaceSqliteNativeBootstrap
{
    private static readonly object Gate = new();
    private static bool _initialized;
    private static string? _nativeLibraryPath;
    private static IntPtr _nativeHandle;

    internal static void EnsureRegistered(WorkspaceDepsManifest? depsManifest)
    {
        if (depsManifest is null)
        {
            return;
        }

        if (!depsManifest.TryResolveNativeLibrary(out var nativePath, GetCandidateNativeFileNames()))
        {
            return;
        }

        RegisterNativeLibrary(nativePath);
    }

    /// <summary>
    ///     Initializes SQLitePCL when the workspace project does not reference EF SQLite (provider override path).
    /// </summary>
    internal static void EnsureBatteriesInitialized(WorkspaceHost host)
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            if (!host.TryResolveAssemblyPath("Microsoft.Data.Sqlite", out var dataSqlitePath))
            {
                throw new InvalidOperationException(
                    "Failed to resolve `Microsoft.Data.Sqlite` for the SQLite provider override."
                    + $"{Environment.NewLine}Restore a project that references Microsoft.EntityFrameworkCore.Sqlite.");
            }

            var coreReference = AssemblyReferenceReader.Read(dataSqlitePath)
                .FirstOrDefault(static reference =>
                    string.Equals(reference.Name, "SQLitePCLRaw.core", StringComparison.OrdinalIgnoreCase));

            if (coreReference is null)
            {
                throw new InvalidOperationException(
                    "`Microsoft.Data.Sqlite` does not reference `SQLitePCLRaw.core`.");
            }

            if (IsSqliteProviderInitialized(coreReference))
            {
                var loadedDataSqlite = AssemblyResolutionHelpers.LoadFromPath(
                    AssemblyLoadContext.Default,
                    dataSqlitePath);
                EnsureMicrosoftDataSqliteStaticConstructor(loadedDataSqlite);
                _initialized = true;
                return;
            }

            TryRegisterNativeFromNuGetCache();

            if (_nativeHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "Failed to load native `e_sqlite3` library required by SQLite."
                    + $"{Environment.NewLine}Restore `Microsoft.EntityFrameworkCore.Sqlite` so the NuGet cache contains"
                    + " `SQLitePCLRaw.lib.e_sqlite3`.");
            }

            if (!host.TryResolveAssemblyPath(coreReference, out var corePath))
            {
                throw new InvalidOperationException(
                    $"Failed to resolve `{coreReference}` required by `Microsoft.Data.Sqlite`.");
            }

            var coreAssembly = AssemblyResolutionHelpers.LoadFromPath(AssemblyLoadContext.Default, corePath);

            var providerReference = new AssemblyName("SQLitePCLRaw.provider.e_sqlite3")
            {
                Version = coreReference.Version,
                ContentType = coreReference.ContentType
            };

            if (coreReference.GetPublicKeyToken() is { } publicKeyToken)
            {
                providerReference.SetPublicKeyToken(publicKeyToken);
            }

            if (!host.TryResolveAssemblyPath(providerReference, out var providerPath))
            {
                if (!host.TryResolveAssemblyPath("SQLitePCLRaw.provider.e_sqlite3", out providerPath))
                {
                    throw new InvalidOperationException(
                        "Failed to load `SQLitePCLRaw.provider.e_sqlite3` required by `Microsoft.Data.Sqlite`.");
                }
            }

            var providerAssembly = AssemblyResolutionHelpers.LoadFromPath(AssemblyLoadContext.Default, providerPath);

            if (!TrySetESqlite3Provider(coreAssembly, providerAssembly))
            {
                throw new InvalidOperationException(
                    "Failed to initialize SQLite native provider (e_sqlite3) for the workspace host."
                    + $"{Environment.NewLine}Ensure `SQLitePCLRaw.provider.e_sqlite3` is available in the NuGet cache"
                    + " (restore a project that references Microsoft.EntityFrameworkCore.Sqlite).");
            }

            var dataSqliteAssembly = AssemblyResolutionHelpers.LoadFromPath(
                AssemblyLoadContext.Default,
                dataSqlitePath);

            EnsureMicrosoftDataSqliteStaticConstructor(dataSqliteAssembly);
            _initialized = true;
        }
    }

    private static bool IsSqliteProviderInitialized(AssemblyName coreReference)
    {
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(assembly.GetName().Name, "SQLitePCLRaw.core", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!AssemblyName.ReferenceMatchesDefinition(coreReference, assembly.GetName()))
                {
                    continue;
                }

                var rawType = assembly.GetType("SQLitePCL.raw", false);

                if (rawType?.GetMethod("get_Provider",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.Invoke(null, null) is not null)
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TrySetESqlite3Provider(Assembly coreAssembly, Assembly providerAssembly)
    {
        var providerType = providerAssembly.GetType("SQLitePCL.SQLite3Provider_e_sqlite3", false);

        if (providerType is null)
        {
            return false;
        }

        var providerInstance = Activator.CreateInstance(providerType);

        if (providerInstance is null)
        {
            return false;
        }

        var rawType = coreAssembly.GetType("SQLitePCL.raw", false);

        if (rawType is null)
        {
            return false;
        }

        var setProvider = rawType.GetMethod(
            "SetProvider",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        if (setProvider is null)
        {
            return false;
        }

        try
        {
            setProvider.Invoke(null, [providerInstance]);

            return rawType.GetMethod("get_Provider", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.Invoke(null, null)
                is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void TryRegisterNativeFromNuGetCache()
    {
        if (_nativeLibraryPath is not null)
        {
            return;
        }

        var nuGetRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            "sqlitepclraw.lib.e_sqlite3");

        if (!Directory.Exists(nuGetRoot))
        {
            return;
        }

        foreach (var versionFolder in Directory.EnumerateDirectories(nuGetRoot)
                     .OrderByDescending(static path => Path.GetFileName(path), StringComparer.Ordinal))
        {
            foreach (var fileName in GetCandidateNativeFileNames())
            {
                foreach (var rid in HostRuntimeIdentifier.GetRuntimeFallbacks())
                {
                    var candidate = Path.Combine(
                        versionFolder,
                        "runtimes",
                        rid,
                        "native",
                        fileName);

                    if (!File.Exists(candidate))
                    {
                        continue;
                    }

                    RegisterNativeLibrary(candidate);
                    return;
                }
            }
        }
    }

    private static void RegisterNativeLibrary(string nativePath)
    {
        lock (Gate)
        {
            if (_nativeLibraryPath is not null)
            {
                return;
            }

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

    private static void EnsureMicrosoftDataSqliteStaticConstructor(Assembly dataSqliteAssembly)
    {
        var connectionType = dataSqliteAssembly.GetType(
            "Microsoft.Data.Sqlite.SqliteConnection",
            false);

        if (connectionType is null)
        {
            throw new InvalidOperationException(
                "Failed to resolve `Microsoft.Data.Sqlite.SqliteConnection` after initializing SQLitePCL.");
        }

        RuntimeHelpers.RunClassConstructor(connectionType.TypeHandle);
    }

    private static IntPtr OnResolvingUnmanagedDll(Assembly assembly, string unmanagedDllName)
    {
        if (_nativeLibraryPath is null || !IsSqliteNativeName(unmanagedDllName))
        {
            return IntPtr.Zero;
        }

        if (_nativeHandle != IntPtr.Zero)
        {
            return _nativeHandle;
        }

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
    {
        return unmanagedDllName.Equals("e_sqlite3", StringComparison.OrdinalIgnoreCase)
               || unmanagedDllName.Equals("libe_sqlite3", StringComparison.OrdinalIgnoreCase)
               || unmanagedDllName.Contains("e_sqlite3", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] GetCandidateNativeFileNames()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return
            [
                "libe_sqlite3.dylib",
                "e_sqlite3.dylib"
            ];
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return
            [
                "libe_sqlite3.so",
                "e_sqlite3.so"
            ];
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ["e_sqlite3.dll"];
        }

        return ["e_sqlite3.dll", "libe_sqlite3.so", "libe_sqlite3.dylib"];
    }
}