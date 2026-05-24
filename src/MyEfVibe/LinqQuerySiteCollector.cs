using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyEfVibe;

internal static class LinqQuerySiteCollector
{
    internal static IReadOnlyList<LinqQuerySite> Collect(
        string efProjectPath,
        string startupProjectPath,
        Type selectedDbContextType) =>
        Collect(efProjectPath, startupProjectPath, selectedDbContextType.Name);

    internal static IReadOnlyList<LinqQuerySite> Collect(
        string efProjectPath,
        string startupProjectPath,
        string selectedContextTypeName)
    {
        var scope = DbContextScanScope.Create(efProjectPath, startupProjectPath, selectedContextTypeName);
        var sites = new List<LinqQuerySite>();
        var projectPaths = LinqProjectSourceWalker.CollectScanProjectPaths(efProjectPath, startupProjectPath);

        foreach (var projectPath in projectPaths)
        {
            var projectDirectory = Path.GetDirectoryName(projectPath)!;

            foreach (var sourcePath in LinqProjectSourceWalker.EnumerateSourceFiles(projectDirectory))
                sites.AddRange(CollectFromFile(sourcePath, scope));
        }

        return sites;
    }

    private static IEnumerable<LinqQuerySite> CollectFromFile(string absolutePath, DbContextScanScope scope)
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
        var containingTypeIndex = DbContextContainingTypeIndex.Build(sourceText, scope);

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var methodName = GetSimpleMethodName(invocation.Expression);

            if (methodName is null || !LinqQueryInvocationNames.ScanTargets.Contains(methodName))
                continue;

            var statement = GetStatementText(invocation);

            if (!LinqEfQueryHeuristics.LooksLikeEfQuery(statement))
                continue;

            var containingTypeName = GetContainingTypeName(invocation);

            if (!DbContextQuerySiteFilter.BelongsToSelectedContext(
                    statement,
                    scope,
                    containingTypeName,
                    containingTypeIndex))
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
        var text = statement?.ToString() ?? node.ToString();

        if (LinqEfQueryHeuristics.LooksLikeEfQuery(text))
            return text;

        var block = node.FirstAncestorOrSelf<BlockSyntax>();

        if (block is not null)
        {
            foreach (var sibling in block.Statements)
            {
                var siblingText = sibling.ToString();

                if (LinqEfQueryHeuristics.LooksLikeEfQuery(siblingText))
                    return siblingText;
            }
        }

        return text;
    }

    private static string? GetContainingTypeName(SyntaxNode node)
    {
        var typeDeclaration = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();

        return typeDeclaration?.Identifier.Text;
    }

    private static string ToPreviewLine(string text)
    {
        var singleLine = text.ReplaceLineEndings(" ").Trim();

        return singleLine.Length <= 120
            ? singleLine
            : singleLine[..117] + "...";
    }
}
