using System.Collections.Generic;

namespace MyEfVibe.VisualStudio.Models;

internal sealed class TablesJsonPayload
{
    public string? DbContext { get; set; }
    public List<TablesJsonEntry>? Tables { get; set; }
}

internal sealed class TablesJsonEntry
{
    public string? DbSet { get; set; }
    public string? EntityType { get; set; }
    public string? EntityTypeFullName { get; set; }
}

internal sealed class DescribeJsonPayload
{
    public bool Success { get; set; }
    public string? DbSet { get; set; }
    public string? EntityType { get; set; }
    public string? EntityTypeFullName { get; set; }
    public List<DescribeJsonMember>? Members { get; set; }
    public string? Error { get; set; }
    public List<string>? KnownEntities { get; set; }
}

internal sealed class DescribeJsonMember
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Nullable { get; set; }
    public string? Notes { get; set; }
}
