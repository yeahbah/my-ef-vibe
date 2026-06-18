using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyEfVibe.Linq;

internal static class LinqLiteScanner
{
    internal static LinqLiteScanResult Scan(
        string efProjectPath,
        string startupProjectPath,
        Type? selectedDbContextType = null,
        string? selectedContextTypeName = null)
    {
        DbContextScanScope? scope = null;

        if (selectedDbContextType is not null)
        {
            scope = DbContextScanScope.Create(efProjectPath, startupProjectPath, selectedDbContextType);
        }
        else if (!string.IsNullOrWhiteSpace(selectedContextTypeName))
        {
            scope = DbContextScanScope.Create(efProjectPath, startupProjectPath, selectedContextTypeName);
        }

        var findings = new List<LinqScanFinding>();
        var filesScanned = 0;
        var projectPaths = LinqProjectSourceWalker.CollectScanProjectPaths(efProjectPath, startupProjectPath);

        foreach (var projectPath in projectPaths)
        {
            var projectDirectory = Path.GetDirectoryName(projectPath)!;

            foreach (var sourcePath in LinqProjectSourceWalker.EnumerateSourceFiles(projectDirectory))
            {
                filesScanned++;
                findings.AddRange(ScanSourceFile(sourcePath, scope));
            }
        }

        return new LinqLiteScanResult(
            filesScanned,
            projectPaths.Count,
            findings.OrderByDescending(static finding => finding.Severity)
                .ThenBy(static finding => finding.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static finding => finding.Line)
                .ThenBy(static finding => finding.RuleId, StringComparer.Ordinal)
                .ToArray());
    }

    private static IEnumerable<LinqScanFinding> ScanSourceFile(string absolutePath, DbContextScanScope? scope)
    {
        string sourceText;

        try
        {
            sourceText = File.ReadAllText(absolutePath);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        var tree = CSharpSyntaxTree.ParseText(
            sourceText,
            path: absolutePath,
            encoding: Encoding.UTF8);

        var root = tree.GetCompilationUnitRoot();
        var containingTypeIndex = scope is null
            ? new DbContextContainingTypeIndex()
            : DbContextContainingTypeIndex.Build(sourceText, scope);
        var instanceIndex = scope is null
            ? DbContextInstanceIdentifierIndex.Empty
            : DbContextInstanceIdentifierIndex.Build(sourceText, scope);
        var findings = new List<LinqScanFinding>();
        var reported = new HashSet<string>(StringComparer.Ordinal);

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
            var containingMethodName = GetContainingMethodName(invocation);

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

            foreach (var (ruleId, message) in LinqQueryWarningRules.AnalyzeSnippet(statement, containingMethodName))
            {
                var key = $"{line}|{ruleId}|{message}";

                if (!reported.Add(key))
                {
                    continue;
                }

                findings.Add(LinqScanFinding.Create(
                    absolutePath,
                    line,
                    preview,
                    ruleId,
                    message));
            }
        }

        foreach (var loop in root.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            AnalyzeLoop(absolutePath, loop, findings, reported, scope, containingTypeIndex, instanceIndex);
        }

        foreach (var loop in root.DescendantNodes().OfType<ForStatementSyntax>())
        {
            AnalyzeLoop(absolutePath, loop, findings, reported, scope, containingTypeIndex, instanceIndex);
        }

        return findings;
    }

    private static void AnalyzeLoop(
        string absolutePath,
        StatementSyntax loopStatement,
        List<LinqScanFinding> findings,
        HashSet<string> reported,
        DbContextScanScope? scope,
        DbContextContainingTypeIndex containingTypeIndex,
        DbContextInstanceIdentifierIndex instanceIndex)
    {
        var bodyText = loopStatement.ToString();

        if (!LooksLikeQueryInLoop(bodyText))
        {
            return;
        }

        var containingTypeName = GetContainingTypeName(loopStatement);

        if (!DbContextQuerySiteFilter.BelongsToSelectedContext(
                bodyText,
                scope,
                containingTypeName,
                containingTypeIndex,
                instanceIndex))
        {
            return;
        }

        var line = loopStatement.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        const string ruleId = "n-plus-one";
        const string message = "Query-like calls inside a loop — possible N+1 pattern.";
        var key = $"{line}|{ruleId}|{message}";

        if (!reported.Add(key))
        {
            return;
        }

        findings.Add(LinqScanFinding.Create(
            absolutePath,
            line,
            ToPreviewLine(bodyText),
            ruleId,
            message));
    }

    private static bool LooksLikeQueryInLoop(string bodyText)
    {
        if (bodyText.Contains(".Where(", StringComparison.Ordinal)
            || bodyText.Contains(".Include(", StringComparison.Ordinal)
            || bodyText.Contains(".ToList(", StringComparison.Ordinal)
            || bodyText.Contains(".First", StringComparison.Ordinal)
            || bodyText.Contains(".Single", StringComparison.Ordinal)
            || bodyText.Contains(".Count(", StringComparison.Ordinal)
            || bodyText.Contains(".Any(", StringComparison.Ordinal))
        {
            return true;
        }

        return bodyText.Contains("db.", StringComparison.Ordinal)
               || bodyText.Contains("DbContext", StringComparison.Ordinal)
               || bodyText.Contains("Set<", StringComparison.Ordinal);
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

    private static string? GetContainingMethodName(SyntaxNode node)
    {
        return node.FirstAncestorOrSelf<MethodDeclarationSyntax>()?.Identifier.Text;
    }

    private static string ToPreviewLine(string text)
    {
        var singleLine = text.ReplaceLineEndings(" ").Trim();

        return singleLine.Length <= 120
            ? singleLine
            : singleLine[..117] + "...";
    }
}