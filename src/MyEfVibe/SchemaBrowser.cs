using System.Reflection;
using Spectre.Console;

namespace MyEfVibe;

internal static class SchemaBrowser
{
    internal static Task WriteTablesAsync(object dbContext, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sets = GetDbSets(dbContext);

        if (sets.Count == 0)
        {
            CliUi.WriteWarning("No DbSet properties found on this context.");
            return Task.CompletedTask;
        }

        var table = new Table().RoundedBorder().BorderColor(Color.Grey);
        table.AddColumn("DbSet");
        table.AddColumn("entity");

        foreach (var entry in sets)
        {
            table.AddRow(entry.DbSet, entry.EntityType);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        return Task.CompletedTask;
    }

    internal static IReadOnlyList<(string DbSet, string EntityType, string? EntityTypeFullName)> GetDbSets(
        object dbContext)
    {
        return DiscoverDbSets(dbContext)
            .Select(set => (set.PropertyName, set.EntityTypeName, set.ElementType.FullName))
            .ToArray();
    }

    internal static async Task<IReadOnlyList<(string DbSet, string EntityType, int? Count)>> GetDbSetCountsAsync(
        object dbContext,
        CancellationToken cancellationToken = default)
    {
        var sets = DiscoverDbSets(dbContext).ToArray();
        var results = new List<(string DbSet, string EntityType, int? Count)>(sets.Length);

        foreach (var set in sets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var countText = await TryCountAsync(dbContext, set, cancellationToken);
            int? count = countText is not null && int.TryParse(countText, out var parsed) ? parsed : null;

            results.Add((set.PropertyName, set.EntityTypeName, count));
        }

        return results;
    }

    private static IEnumerable<(string PropertyName, string EntityTypeName, Type ElementType)> DiscoverDbSets(
        object dbContext)
    {
        foreach (var property in dbContext.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.PropertyType.IsGenericType)
            {
                continue;
            }

            if (!typeof(IQueryable).IsAssignableFrom(property.PropertyType))
            {
                continue;
            }

            var elementType = property.PropertyType.GetGenericArguments()[0];

            yield return (property.Name, elementType.Name, elementType);
        }
    }

    private static async Task<string?> TryCountAsync(
        object dbContext,
        (string PropertyName, string EntityTypeName, Type ElementType) set,
        CancellationToken cancellationToken)
    {
        try
        {
            var dbSet = dbContext.GetType().GetProperty(set.PropertyName)?.GetValue(dbContext);

            if (dbSet is null)
            {
                return null;
            }

            var count = await Task.Run(() => InvokeCount(dbSet, set.ElementType), cancellationToken);

            return count?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static int? InvokeCount(object queryable, Type elementType)
    {
        var countMethod = typeof(Queryable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(Queryable.Count) && method.GetParameters().Length == 1)
            .MakeGenericMethod(elementType);

        return (int?)countMethod.Invoke(null, [queryable]);
    }
}