using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyEfVibe.Linq;

internal static class LinqQuerySiteCollector
{
    internal static IReadOnlyList<LinqQuerySite> Collect(
        string efProjectPath,
        string startupProjectPath,
        Type selectedDbContextType)
    {
        return Collect(efProjectPath, startupProjectPath, selectedDbContextType.Name);
    }

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
            {
                sites.AddRange(CollectFromFile(sourcePath, scope));
            }
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
            encoding: Encoding.UTF8);

        var root = tree.GetCompilationUnitRoot();
        var containingTypeIndex = DbContextContainingTypeIndex.Build(sourceText, scope);
        var instanceIndex = DbContextInstanceIdentifierIndex.Build(sourceText, scope);

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var methodName = GetSimpleMethodName(invocation.Expression);

            if (methodName is null || !LinqQueryInvocationNames.ScanTargets.Contains(methodName))
            {
                continue;
            }

            var statement = LinqScanStatementResolver.Resolve(invocation);

            if (!LinqEfQueryHeuristics.LooksLikeEfQuery(statement, scope, instanceIndex))
            {
                continue;
            }

            var containingTypeName = GetContainingTypeName(invocation);

            if (!DbContextQuerySiteFilter.BelongsToSelectedContext(
                    statement,
                    scope,
                    containingTypeName,
                    containingTypeIndex,
                    instanceIndex))
            {
                continue;
            }

            var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var preview = ToPreviewLine(statement);

            yield return new LinqQuerySite(
                absolutePath,
                line,
                preview,
                statement,
                instanceIndex.SelectedContextIdentifiers);
        }
    }

    private static string? GetSimpleMethodName(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => null
        };
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