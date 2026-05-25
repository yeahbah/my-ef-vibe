using System.Collections.Generic;

namespace MyEfVibe.VisualStudio.Services;

internal sealed class CliInvocation
{
    public string Command { get; set; } = "efvibe";
    public List<string> PrefixArgs { get; } = new();
}
