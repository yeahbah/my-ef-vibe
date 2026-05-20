using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyEfVibe;

internal static class LinqQuerySiteCollector
{
    private static readonly HashSet<string> QueryMethodNames = new(StringComparer.Ordinal)
    {
        "AsEnumerable",
        "ToList",
        "ToListAsync",
        "ToArray",
        "ToArrayAsync",
        "Include",
        "ThenInclude",
        "FromSql",
        "FromSqlRaw",
        "ExecuteSqlRaw",
        "Single",
        "SingleAsync",
        "SingleOrDefault",
        "SingleOrDefaultAsync",
        "First",
        "FirstAsync",
        "FirstOrDefault",
        "FirstOrDefaultAsync",
        "Count",
        "CountAsync",
        "Any",
        "AnyAsync",
        "All",
        "AllAsync",
    };

    internal static IReadOnlyList<LinqQuerySite> Collect(string efProjectPath, string startupProjectPath)
    {
        var sites = new List<LinqQuerySite>();
        var projectPaths = LinqProjectSourceWalker.CollectScanProjectPaths(efProjectPath, startupProjectPath);

        foreach (var projectPath in projectPaths)
        {
            var projectDirectory = Path.GetDirectoryName(projectPath)!;

            foreach (var sourcePath in LinqProjectSourceWalker.EnumerateSourceFiles(projectDirectory))
                sites.AddRange(CollectFromFile(sourcePath));
        }

        return sites;
    }

    private static IEnumerable<LinqQuerySite> CollectFromFile(string absolutePath)
    {
        string sourceText;

        try
        {
            sourceText = File.ReadAllText(absolutePath);
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        var tree = CSharpSyntaxTree.ParseText(
            sourceText,
            path: absolutePath,
            encoding: System.Text.Encoding.UTF8);

        var root = tree.GetCompilationUnitRoot();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var methodName = GetSimpleMethodName(invocation.Expression);

            if (methodName is null || !QueryMethodNames.Contains(methodName))
                continue;

            var statement = GetStatementText(invocation);

            if (!LinqEfQueryHeuristics.LooksLikeEfQuery(statement))
                continue;

            var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var preview = ToPreviewLine(statement);

            yield return new LinqQuerySite(absolutePath, line, preview, statement);
        }
    }

    private static string? GetSimpleMethodName(ExpressionSyntax expression) =>
        expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => null,
        };

    private static string GetStatementText(SyntaxNode node)
    {
        var statement = node.FirstAncestorOrSelf<StatementSyntax>();

        return statement?.ToString() ?? node.ToString();
    }

    private static string ToPreviewLine(string text)
    {
        var singleLine = text.ReplaceLineEndings(" ").Trim();

        return singleLine.Length <= 120
            ? singleLine
            : singleLine[..117] + "...";
    }
}
