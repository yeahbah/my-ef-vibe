namespace MyEfVibe.Workspace;

internal static class NativeLibraryFileNames
{
    internal static bool IsNativeBinaryFileName(string fileName)
    {
        if (fileName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
               && fileName.Contains("e_sqlite3", StringComparison.OrdinalIgnoreCase);
    }
}
