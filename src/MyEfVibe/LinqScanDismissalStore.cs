using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyEfVibe;

internal static class LinqScanDismissalStore
{
    internal const string FileName = "myefvibe-scan-dismissals.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static string GetPath(string sessionDirectory)
    {
        return Path.Combine(SessionPaths.EnsureSessionDirectory(sessionDirectory), FileName);
    }

    internal static (IReadOnlyList<LinqScanFinding> Findings, int SkippedCount) FilterFindings(
        IReadOnlyList<LinqScanFinding> findings,
        string sessionDirectory)
    {
        var dismissedKeys = LoadDismissedKeys(sessionDirectory);

        if (dismissedKeys.Count == 0)
        {
            return (findings, 0);
        }

        var kept = new List<LinqScanFinding>(findings.Count);
        var skipped = 0;

        foreach (var finding in findings)
        {
            if (dismissedKeys.Contains(finding.GetDismissalKey()))
            {
                skipped++;
                continue;
            }

            kept.Add(finding);
        }

        return (kept, skipped);
    }

    internal static void Dismiss(string sessionDirectory, LinqScanFinding finding, string? note)
    {
        var path = GetPath(sessionDirectory);
        var document = LoadDocument(path);
        var key = finding.GetDismissalKey();

        document.Dismissals.RemoveAll(entry => string.Equals(entry.Key, key, StringComparison.Ordinal));

        document.Dismissals.Add(new LinqScanDismissalEntry(
            key,
            Path.GetFullPath(finding.FilePath),
            finding.Line,
            finding.RuleId,
            string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            DateTimeOffset.Now));

        SaveDocument(path, document);
    }

    private static HashSet<string> LoadDismissedKeys(string sessionDirectory)
    {
        var path = GetPath(sessionDirectory);
        var document = LoadDocument(path);

        return document.Dismissals
            .Select(static entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static LinqScanDismissalsDocument LoadDocument(string path)
    {
        if (!File.Exists(path))
        {
            return new LinqScanDismissalsDocument(1, []);
        }

        try
        {
            var json = File.ReadAllText(path);

            return JsonSerializer.Deserialize<LinqScanDismissalsDocument>(json, JsonOptions)
                   ?? new LinqScanDismissalsDocument(1, []);
        }
        catch (JsonException)
        {
            return new LinqScanDismissalsDocument(1, []);
        }
        catch (IOException)
        {
            return new LinqScanDismissalsDocument(1, []);
        }
    }

    private static void SaveDocument(string path, LinqScanDismissalsDocument document)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
    }
}

internal sealed record LinqScanDismissalsDocument(int Version, List<LinqScanDismissalEntry> Dismissals);

internal sealed record LinqScanDismissalEntry(
    string Key,
    string FilePath,
    int Line,
    string RuleId,
    string? Note,
    DateTimeOffset DismissedAt);