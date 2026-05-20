using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyEfVibe;

internal static class LinqLiteScanner
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

    internal static LinqLiteScanResult Scan(string efProjectPath, string startupProjectPath)
    {
        var findings = new List<LinqScanFinding>();
        var filesScanned = 0;
        var projectPaths = LinqProjectSourceWalker.CollectScanProjectPaths(efProjectPath, startupProjectPath);

        foreach (var projectPath in projectPaths)
        {
            var projectDirectory = Path.GetDirectoryName(projectPath)!;

            foreach (var sourcePath in LinqProjectSourceWalker.EnumerateSourceFiles(projectDirectory))
            {
                filesScanned++;
                findings.AddRange(ScanSourceFile(sourcePath));
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

    private static IEnumerable<LinqScanFinding> ScanSourceFile(string absolutePath)
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
            encoding: System.Text.Encoding.UTF8);

        var root = tree.GetCompilationUnitRoot();
        var findings = new List<LinqScanFinding>();
        var reported = new HashSet<string>(StringComparer.Ordinal);

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

            foreach (var (ruleId, message) in LinqQueryWarningRules.AnalyzeSnippet(statement))
            {
                var key = $"{line}|{ruleId}|{message}";

                if (!reported.Add(key))
                    continue;

                findings.Add(LinqScanFinding.Create(
                    absolutePath,
                    line,
                    preview,
                    ruleId,
                    message));
            }
        }

        foreach (var loop in root.DescendantNodes().OfType<ForEachStatementSyntax>())
            AnalyzeLoop(absolutePath, loop, findings, reported);

        foreach (var loop in root.DescendantNodes().OfType<ForStatementSyntax>())
            AnalyzeLoop(absolutePath, loop, findings, reported);

        return findings;
    }

    private static void AnalyzeLoop(
        string absolutePath,
        StatementSyntax loopStatement,
        List<LinqScanFinding> findings,
        HashSet<string> reported)
    {
        var bodyText = loopStatement.ToString();

        if (!LooksLikeQueryInLoop(bodyText))
            return;

        var line = loopStatement.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        const string ruleId = "n-plus-one";
        const string message = "Query-like calls inside a loop — possible N+1 pattern.";
        var key = $"{line}|{ruleId}|{message}";

        if (!reported.Add(key))
            return;

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
            return true;

        return bodyText.Contains("db.", StringComparison.Ordinal)
               || bodyText.Contains("DbContext", StringComparison.Ordinal)
               || bodyText.Contains("Set<", StringComparison.Ordinal);
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
