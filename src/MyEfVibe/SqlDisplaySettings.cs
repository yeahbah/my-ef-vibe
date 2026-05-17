namespace MyEfVibe;

internal sealed class SqlDisplaySettings
{
    internal bool ShowSql { get; set; } = true;

    internal void Toggle() => ShowSql = !ShowSql;
}
