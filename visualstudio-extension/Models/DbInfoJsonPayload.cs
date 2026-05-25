using System.Collections.Generic;

namespace MyEfVibe.VisualStudio.Models;

internal sealed class DbInfoJsonPayload
{
    public string? DbContext { get; set; }
    public List<DbInfoJsonEntry>? Entries { get; set; }
}

internal sealed class DbInfoJsonEntry
{
    public string? Key { get; set; }
    public string? Value { get; set; }
}
