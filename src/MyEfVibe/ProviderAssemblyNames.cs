namespace MyEfVibe;

internal static class ProviderAssemblyNames
{
    internal static IEnumerable<string> For(MyEfVibeProvider provider) =>
        provider switch
        {
            MyEfVibeProvider.SqlServer => ["Microsoft.EntityFrameworkCore.SqlServer"],
            MyEfVibeProvider.Npgsql => ["Npgsql.EntityFrameworkCore.PostgreSQL"],
            MyEfVibeProvider.Sqlite => ["Microsoft.EntityFrameworkCore.Sqlite"],
            MyEfVibeProvider.Oracle => ["Oracle.EntityFrameworkCore"],
            MyEfVibeProvider.MySql or MyEfVibeProvider.MariaDb =>
            [
                "Pomelo.EntityFrameworkCore.MySql",
                "MySql.EntityFrameworkCore",
            ],
            _ => [],
        };
}
