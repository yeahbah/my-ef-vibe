using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyEfVibe;

internal static class LinqScanNoteStore
{
    internal const string FileName = "myefvibe-scan-notes.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static string GetPath(string sessionDirectory) =>
        Path.Combine(SessionPaths.EnsureSessionDirectory(sessionDirectory), FileName);

    internal static IReadOnlyList<LinqScanFinding> ApplyNotes(
        IReadOnlyList<LinqScanFinding> findings,
        string sessionDirectory)
    {
        var notes = LoadNoteMap(sessionDirectory);

        if (notes.Count == 0)
            return findings;

        return findings
            .Select(finding =>
                notes.TryGetValue(finding.GetDismissalKey(), out var note)
                    ? finding with { SavedNote = note }
                    : finding)
            .ToArray();
    }

    internal static void SaveNote(string sessionDirectory, LinqScanFinding finding, string note)
    {
        var trimmed = note.Trim();

        if (trimmed.Length == 0)
            throw new ArgumentException("Note text is required.", nameof(note));

        var path = GetPath(sessionDirectory);
        var document = LoadDocument(path);
        var key = finding.GetDismissalKey();

        document.Notes.RemoveAll(entry => string.Equals(entry.Key, key, StringComparison.Ordinal));

        document.Notes.Add(new LinqScanNoteEntry(
            key,
            Path.GetFullPath(finding.FilePath),
            finding.Line,
            finding.RuleId,
            trimmed,
            DateTimeOffset.Now));

        SaveDocument(path, document);
    }

    private static Dictionary<string, string> LoadNoteMap(string sessionDirectory)
    {
        var path = GetPath(sessionDirectory);
        var document = LoadDocument(path);

        return document.Notes
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.Note))
            .ToDictionary(static entry => entry.Key, static entry => entry.Note, StringComparer.Ordinal);
    }

    private static LinqScanNotesDocument LoadDocument(string path)
    {
        if (!File.Exists(path))
            return new LinqScanNotesDocument(1, []);

        try
        {
            var json = File.ReadAllText(path);

            return JsonSerializer.Deserialize<LinqScanNotesDocument>(json, JsonOptions)
                ?? new LinqScanNotesDocument(1, []);
        }
        catch (JsonException)
        {
            return new LinqScanNotesDocument(1, []);
        }
        catch (IOException)
        {
            return new LinqScanNotesDocument(1, []);
        }
    }

    private static void SaveDocument(string path, LinqScanNotesDocument document) =>
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
}

internal sealed record LinqScanNotesDocument(int Version, List<LinqScanNoteEntry> Notes);

internal sealed record LinqScanNoteEntry(
    string Key,
    string FilePath,
    int Line,
    string RuleId,
    string Note,
    DateTimeOffset UpdatedAt);
