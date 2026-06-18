using System.Collections;
using System.Reflection;
using Spectre.Console;

namespace MyEfVibe.Reporters;

internal static class ChangeTrackerReporter
{
    internal static void Write(object dbContext)
    {
        var tracker = dbContext.GetType().GetProperty("ChangeTracker")?.GetValue(dbContext);

        if (tracker is null)
        {
            CliUi.WriteWarning("ChangeTracker not available on this context.");
            return;
        }

        var entries = EnumerateEntries(tracker).ToArray();

        if (entries.Length == 0)
        {
            AnsiConsole.MarkupLine("[grey]No tracked entities.[/]");
            AnsiConsole.WriteLine();
            return;
        }

        var grouped = entries
            .GroupBy(static entry => entry.State, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase);

        var table = new Table().RoundedBorder().BorderColor(Color.Grey);
        table.AddColumn("state");
        table.AddColumn("count");
        table.AddColumn("types");

        foreach (var group in grouped)
        {
            var types = group
                .Select(static entry => entry.EntityType)
                .Distinct(StringComparer.Ordinal)
                .Take(5);

            table.AddRow(
                group.Key,
                group.Count().ToString(),
                string.Join(", ", types) +
                (group.Select(static e => e.EntityType).Distinct().Count() > 5 ? ", …" : ""));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static IEnumerable<(string State, string EntityType)> EnumerateEntries(object changeTracker)
    {
        // ChangeTracker has Entries() and Entries<TEntity>() — both are parameterless.
        var entriesMethod = changeTracker.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method =>
                string.Equals(method.Name, "Entries", StringComparison.Ordinal)
                && !method.IsGenericMethod
                && method.GetParameters().Length == 0);

        if (entriesMethod is null)
        {
            yield break;
        }

        if (entriesMethod.Invoke(changeTracker, null) is not IEnumerable entries)
        {
            yield break;
        }

        foreach (var entry in entries)
        {
            if (entry is null)
            {
                continue;
            }

            var entryType = entry.GetType();
            var state = entryType.GetProperty("State")?.GetValue(entry)?.ToString() ?? "Unknown";
            var entity = entryType.GetProperty("Entity")?.GetValue(entry);
            var entityType = entity?.GetType().Name ?? "object";

            yield return (state, entityType);
        }
    }
}