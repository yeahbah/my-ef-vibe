using System.Collections;
using System.Reflection;
using Spectre.Console;

namespace MyEfVibe;

internal static class EntityDescriptor
{
    internal static void Write(object dbContext, string entityName)
    {
        if (string.IsNullOrWhiteSpace(entityName))
        {
            CliUi.WriteWarning("Usage: :describe <entity>");
            return;
        }

        var dbSets = EnumerateDbSetEntities(dbContext).ToArray();

        if (dbSets.Length == 0)
        {
            CliUi.WriteWarning("No DbSet properties found on this context.");
            return;
        }

        switch (TryResolveEntity(dbSets, entityName.Trim(), out var resolved))
        {
            case EntityResolveResult.NotFound:
                CliUi.WriteWarning(
                    $"Entity `{entityName}` was not found."
                    + $"{Environment.NewLine}Known entities (DbSet · type):"
                    + $"{Environment.NewLine}{string.Join(Environment.NewLine, dbSets.Select(static entry => $" - {entry.DbSetName} · {entry.EntityType.Name}"))}");
                return;

            case EntityResolveResult.Ambiguous:
                CliUi.WriteWarning(
                    $"Multiple entities match `{entityName}`. Use a DbSet name or full type name."
                    + $"{Environment.NewLine}{string.Join(Environment.NewLine, resolved.AmbiguousMatches!.Select(static entry => $" - {entry.DbSetName} · {entry.EntityType.Name}"))}");
                return;

            case EntityResolveResult.Found:
                break;

            default:
                return;
        }

        var match = resolved.Match!.Value;

        var entityType = match.EntityType;
        var modelEntity = TryFindModelEntity(dbContext, entityType);
        var entityTypeNames = dbSets.Select(static entry => entry.EntityType).ToHashSet();

        var table = new Table().RoundedBorder().BorderColor(Color.Grey);
        table.AddColumn(new TableColumn("[grey]Member[/]").NoWrap());
        table.AddColumn(new TableColumn("[grey]Type[/]").NoWrap());
        table.AddColumn("nullable");
        table.AddColumn("[grey]Notes[/]");

        foreach (var member in DescribeMembers(entityType, modelEntity, entityTypeNames))
        {
            table.AddRow(
                Markup.Escape(member.Name),
                Markup.Escape(member.TypeDisplay),
                Markup.Escape(member.Nullable),
                Markup.Escape(member.Notes));
        }

        AnsiConsole.Write(
            new Panel(table)
            {
                Header = new PanelHeader(
                    $"[bold]{Markup.Escape(match.DbSetName)}[/] [grey]→[/] [cyan]{Markup.Escape(entityType.FullName ?? entityType.Name)}[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Grey),
                Padding = new Padding(1, 0, 1, 0)
            });

        AnsiConsole.WriteLine();
    }

    internal static EntityResolveResult TryResolveEntity(
        IReadOnlyList<(string DbSetName, Type EntityType)> dbSets,
        string query,
        out EntityResolveOutcome outcome)
    {
        outcome = new EntityResolveOutcome();

        foreach (var entry in dbSets)
        {
            if (string.Equals(entry.DbSetName, query, StringComparison.OrdinalIgnoreCase))
            {
                outcome = new EntityResolveOutcome { Match = entry };
                return EntityResolveResult.Found;
            }
        }

        var exactTypeName = dbSets.Where(entry =>
                string.Equals(entry.EntityType.Name, query, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.EntityType.FullName, query, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (exactTypeName.Length == 1)
        {
            outcome = new EntityResolveOutcome { Match = exactTypeName[0] };
            return EntityResolveResult.Found;
        }

        if (exactTypeName.Length > 1)
        {
            outcome = new EntityResolveOutcome { AmbiguousMatches = exactTypeName };
            return EntityResolveResult.Ambiguous;
        }

        var suffixMatches = dbSets.Where(entry =>
                entry.EntityType.Name.EndsWith(query, StringComparison.OrdinalIgnoreCase)
                || (entry.EntityType.FullName?.EndsWith(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToArray();

        if (suffixMatches.Length == 1)
        {
            outcome = new EntityResolveOutcome { Match = suffixMatches[0] };
            return EntityResolveResult.Found;
        }

        if (suffixMatches.Length > 1)
        {
            outcome = new EntityResolveOutcome { AmbiguousMatches = suffixMatches };
            return EntityResolveResult.Ambiguous;
        }

        var containsMatches = dbSets.Where(entry =>
                entry.EntityType.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (entry.EntityType.FullName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                || entry.DbSetName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (containsMatches.Length == 1)
        {
            outcome = new EntityResolveOutcome { Match = containsMatches[0] };
            return EntityResolveResult.Found;
        }

        if (containsMatches.Length > 1)
        {
            outcome = new EntityResolveOutcome { AmbiguousMatches = containsMatches };
            return EntityResolveResult.Ambiguous;
        }

        return EntityResolveResult.NotFound;
    }

    internal static IEnumerable<(string DbSetName, Type EntityType)> EnumerateDbSetEntities(object dbContext)
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

            yield return (property.Name, elementType);
        }
    }

    internal static IEnumerable<MemberRow> DescribeMembers(
        Type entityType,
        object? modelEntity,
        HashSet<Type> entityTypeNames)
    {
        var modelProperties = modelEntity is null
            ? null
            : EnumerateModelProperties(modelEntity).ToDictionary(
                static property => property.Name,
                static property => property,
                StringComparer.Ordinal);

        var scalarRows = new List<MemberRow>();
        var navigationRows = new List<MemberRow>();

        foreach (var property in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var isNavigation = IsNavigationProperty(property, entityTypeNames);
            var row = BuildMemberRow(property.Name, property.PropertyType, isNavigation, modelProperties);

            if (isNavigation)
            {
                navigationRows.Add(row);
            }
            else
            {
                scalarRows.Add(row);
            }
        }

        foreach (var row in scalarRows.OrderBy(static member => member.Name, StringComparer.OrdinalIgnoreCase))
        {
            yield return row;
        }

        foreach (var row in navigationRows.OrderBy(static member => member.Name, StringComparer.OrdinalIgnoreCase))
        {
            yield return row;
        }
    }

    private static MemberRow BuildMemberRow(
        string name,
        Type clrType,
        bool isNavigation,
        IReadOnlyDictionary<string, ModelPropertyInfo>? modelProperties)
    {
        var underlying = Nullable.GetUnderlyingType(clrType) ?? clrType;
        var isNullable = clrType != underlying
                         || (modelProperties?.TryGetValue(name, out var modelProperty) == true &&
                             modelProperty.IsNullable);

        var notes = new List<string>();

        if (isNavigation)
        {
            notes.Add("navigation");
        }

        if (modelProperties?.TryGetValue(name, out var mapped) == true)
        {
            if (mapped.IsPrimaryKey)
            {
                notes.Add("PK");
            }

            if (mapped.IsForeignKey)
            {
                notes.Add("FK");
            }

            if (!string.IsNullOrWhiteSpace(mapped.ColumnName)
                && !string.Equals(mapped.ColumnName, name, StringComparison.OrdinalIgnoreCase))
            {
                notes.Add($"column: {mapped.ColumnName}");
            }

            if (mapped.MaxLength is int maxLength)
            {
                notes.Add($"max {maxLength}");
            }
        }

        return new MemberRow(
            name,
            FormatTypeName(clrType),
            isNullable ? "yes" : "no",
            notes.Count == 0 ? string.Empty : string.Join(", ", notes));
    }

    private static bool IsNavigationProperty(PropertyInfo property, HashSet<Type> entityTypeNames)
    {
        var propertyType = property.PropertyType;

        if (entityTypeNames.Contains(propertyType))
        {
            return true;
        }

        if (propertyType.IsGenericType && typeof(IEnumerable).IsAssignableFrom(propertyType))
        {
            var elementType = propertyType.GetGenericArguments()[0];

            if (entityTypeNames.Contains(elementType))
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatTypeName(Type type)
    {
        if (type == typeof(string))
        {
            return "string";
        }

        if (type == typeof(int))
        {
            return "int";
        }

        if (type == typeof(long))
        {
            return "long";
        }

        if (type == typeof(bool))
        {
            return "bool";
        }

        if (type == typeof(decimal))
        {
            return "decimal";
        }

        if (type == typeof(DateTime))
        {
            return "DateTime";
        }

        if (type == typeof(DateTimeOffset))
        {
            return "DateTimeOffset";
        }

        if (type == typeof(Guid))
        {
            return "Guid";
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return $"{FormatTypeName(underlying)}?";
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();

            if (definition == typeof(List<>)
                || definition == typeof(IList<>)
                || definition == typeof(ICollection<>)
                || definition == typeof(IEnumerable<>))
            {
                return $"{FormatTypeName(type.GetGenericArguments()[0])}[]";
            }
        }

        return type.Name;
    }

    internal static object? TryFindModelEntity(object dbContext, Type entityType)
    {
        var model = dbContext.GetType().GetProperty("Model")?.GetValue(dbContext);

        if (model is null)
        {
            return null;
        }

        foreach (var method in model.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (!IsMemberNameMatch(method.Name, "FindEntityType"))
            {
                continue;
            }

            var parameters = method.GetParameters();

            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(Type))
            {
                continue;
            }

            try
            {
                return method.Invoke(model, [entityType]);
            }
            catch
            {
                return null;
            }
        }

        foreach (var interfaceType in model.GetType().GetInterfaces())
        {
            var method = interfaceType.GetMethod("FindEntityType", BindingFlags.Public | BindingFlags.Instance);
            var parameters = method?.GetParameters();

            if (method is null
                || parameters?.Length != 1
                || parameters[0].ParameterType != typeof(Type))
            {
                continue;
            }

            try
            {
                return method.Invoke(model, [entityType]);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static bool IsMemberNameMatch(string actualName, string expectedName)
    {
        return string.Equals(actualName, expectedName, StringComparison.Ordinal)
               || actualName.EndsWith($".{expectedName}", StringComparison.Ordinal);
    }

    private static IEnumerable<ModelPropertyInfo> EnumerateModelProperties(object modelEntity)
    {
        IEnumerable? properties = null;

        foreach (var method in modelEntity.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!string.Equals(method.Name, "GetProperties", StringComparison.Ordinal)
                || method.GetParameters().Length != 0)
            {
                continue;
            }

            properties = method.Invoke(modelEntity, null) as IEnumerable;

            if (properties is not null)
            {
                break;
            }
        }

        if (properties is null)
        {
            yield break;
        }

        foreach (var property in properties)
        {
            if (property is null)
            {
                continue;
            }

            var propertyType = property.GetType();
            var name = propertyType.GetProperty("Name")?.GetValue(property) as string ?? string.Empty;

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            yield return new ModelPropertyInfo(
                name,
                ReadBool(property, "IsPrimaryKey"),
                ReadBool(property, "IsForeignKey"),
                ReadBool(property, "IsNullable"),
                ReadString(property, "GetColumnName"),
                ReadInt(property, "GetMaxLength"));
        }
    }

    private static bool ReadBool(object target, string memberName)
    {
        var property = target.GetType().GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);

        return property?.GetValue(target) is true;
    }

    private static string? ReadString(object target, string methodName)
    {
        var method = target.GetType()
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);

        return method?.Invoke(target, null) as string;
    }

    private static int? ReadInt(object target, string methodName)
    {
        var method = target.GetType()
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);

        return method?.Invoke(target, null) as int?;
    }

    internal enum EntityResolveResult
    {
        NotFound,
        Found,
        Ambiguous
    }

    internal sealed class EntityResolveOutcome
    {
        internal (string DbSetName, Type EntityType)? Match { get; init; }
        internal IReadOnlyList<(string DbSetName, Type EntityType)>? AmbiguousMatches { get; init; }
    }

    internal sealed record MemberRow(string Name, string TypeDisplay, string Nullable, string Notes);

    private sealed record ModelPropertyInfo(
        string Name,
        bool IsPrimaryKey,
        bool IsForeignKey,
        bool IsNullable,
        string? ColumnName,
        int? MaxLength);
}