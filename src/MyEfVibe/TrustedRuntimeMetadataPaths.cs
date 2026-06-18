using System.Collections.Immutable;

namespace MyEfVibe;

internal static class TrustedRuntimeMetadataPaths
{
    internal static ImmutableArray<string> Discover()
    {
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is not string payload)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>();

        foreach (var token in payload.Split(['|', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = token.Trim();

            if (File.Exists(trimmed))
            {
                builder.Add(trimmed);
            }
        }

        return builder.ToImmutable();
    }
}