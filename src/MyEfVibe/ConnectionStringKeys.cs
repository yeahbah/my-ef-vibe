namespace MyEfVibe;

internal static class ConnectionStringKeys
{
    internal static readonly string[] PreferredNames =
    [
        "DefaultConnection",
        "Postgres",
        "Sqlite",
        "Database",
    ];

    internal static string FlatKey(string name) => $"ConnectionStrings:{name}";
}
