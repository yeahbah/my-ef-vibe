using System.Linq.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

internal static class ProbeTestHelper
{
    internal static void AssertParsesAsScript(string expression)
    {
        var tree = CSharpSyntaxTree.ParseText(
            expression,
            CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));

        var errors = tree.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.ToString())
            .ToArray();

        Assert.Empty(errors);
    }

    internal static void AssertCompilesWithDbContext(string probeExpression, Type dbContextType)
    {
        var contextName = dbContextType.Name;
        var namespaceName = dbContextType.Namespace;
        var namespaceUsing = string.IsNullOrWhiteSpace(namespaceName)
            ? string.Empty
            : $"using {namespaceName};{Environment.NewLine}";

        var wrapper = $$"""
                        using System.Linq;
                        using System.Linq.Expressions;
                        using Microsoft.EntityFrameworkCore;
                        {{namespaceUsing}}public static class ProbeCompileHarness
                        {
                            public static void Run({{contextName}} db)
                            {
                                _ = {{probeExpression}};
                            }
                        }
                        """;

        var tree = CSharpSyntaxTree.ParseText(wrapper);
        var compilation = CSharpCompilation.Create(
            $"probe-compile-{Guid.NewGuid():N}",
            [tree],
            ReferenceAssemblies.For(dbContextType),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.ToString())
            .ToArray();

        Assert.Empty(errors);
    }

    internal static string CollapseWhitespace(string value)
    {
        return string.Join(
            ' ',
            value.ReplaceLineEndings(" ")
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static class ReferenceAssemblies
    {
        internal static MetadataReference[] For(Type dbContextType)
        {
            var shared = new[]
            {
                typeof(object).Assembly,
                typeof(Enumerable).Assembly,
                typeof(Expression).Assembly,
                typeof(DbContext).Assembly,
                typeof(ReplQueryableRuntime).Assembly,
                dbContextType.Assembly
            };

            return shared
                .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
                .ToArray();
        }
    }
}