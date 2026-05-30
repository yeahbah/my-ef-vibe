using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace MyEfVibe;

internal static class AssemblyReferenceReader
{
    internal static IEnumerable<AssemblyName> Read(string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
        {
            yield break;
        }

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        if (!peReader.HasMetadata)
        {
            yield break;
        }

        var reader = peReader.GetMetadataReader();

        foreach (var handle in reader.AssemblyReferences)
        {
            var assemblyReference = reader.GetAssemblyReference(handle);
            var name = reader.GetString(assemblyReference.Name);

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var culture = reader.GetString(assemblyReference.Culture);

            if (string.IsNullOrEmpty(culture))
            {
                culture = "neutral";
            }

            var token = reader.GetBlobBytes(assemblyReference.PublicKeyOrToken);

            var displayName = token.Length == 0
                ? $"{name}, Version={assemblyReference.Version}, Culture={culture}"
                : $"{name}, Version={assemblyReference.Version}, Culture={culture}, PublicKeyToken={FormatPublicKeyToken(token)}";

            yield return new AssemblyName(displayName);
        }
    }

    private static string FormatPublicKeyToken(ReadOnlySpan<byte> token)
    {
        var builder = new StringBuilder(token.Length * 2);

        foreach (var value in token)
        {
            builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }
}