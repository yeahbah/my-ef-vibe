namespace MyEfVibe;

internal enum MyEfVibeProvider
{
    SqlServer,
    Npgsql,
    Sqlite,
    Oracle,
    MySql,
    MariaDb
}

internal static class MyEfVibeProviderExtensions
{
    internal static bool UsesMySqlProtocol(this MyEfVibeProvider provider)
    {
        return provider is MyEfVibeProvider.MySql or MyEfVibeProvider.MariaDb;
    }
}