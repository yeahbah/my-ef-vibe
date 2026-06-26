using System.Collections;
using System.Reflection;
using System.Text;

namespace MyEfVibe;

internal static class ErDiagramMermaidBuilder
{
    private const string IModelFullName = "Microsoft.EntityFrameworkCore.Metadata.IModel";
    private const string IEntityTypeFullName = "Microsoft.EntityFrameworkCore.Metadata.IEntityType";
    private const string IForeignKeyFullName = "Microsoft.EntityFrameworkCore.Metadata.IForeignKey";
    private const string IPropertyFullName = "Microsoft.EntityFrameworkCore.Metadata.IProperty";

    internal static string Build(object dbContext, string? entityName = null)
    {
        var dbSets = EntityDescriptor.EnumerateDbSetEntities(dbContext).ToArray();
        var entityTypeNames = dbSets.Select(static entry => entry.EntityType).ToHashSet();
        var model = TryGetModel(dbContext);
        HashSet<Type>? includedClrTypes = null;

        if (!string.IsNullOrWhiteSpace(entityName))
        {
            switch (EntityDescriptor.TryResolveEntity(dbSets, entityName.Trim(), out var resolved))
            {
                case EntityDescriptor.EntityResolveResult.Found:
                    includedClrTypes = CollectNeighborClrTypes(resolved.Match!.Value.EntityType, model);
                    break;

                case EntityDescriptor.EntityResolveResult.Ambiguous:
                case EntityDescriptor.EntityResolveResult.NotFound:
                    throw new InvalidOperationException(
                        $"Entity `{entityName}` was not found. Use a DbSet name or entity type name.");
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine("erDiagram");

        if (model is null)
        {
            foreach (var (_, entityType) in dbSets)
            {
                if (includedClrTypes is not null && !includedClrTypes.Contains(entityType))
                {
                    continue;
                }

                AppendEntityBlock(builder, SanitizeEntityName(entityType.Name), entityType, null, entityTypeNames);
            }

            return builder.ToString().TrimEnd();
        }

        var modelEntities = EnumerateEntityTypes(model)
            .Where(static entity => !IsOwned(entity))
            .ToArray();

        var labelByClrType = new Dictionary<Type, string>();

        foreach (var modelEntity in modelEntities)
        {
            var clrType = GetClrType(modelEntity);

            if (clrType is null || (includedClrTypes is not null && !includedClrTypes.Contains(clrType)))
            {
                continue;
            }

            labelByClrType[clrType] = SanitizeEntityName(clrType.Name);
        }

        ResolveLabelCollisions(labelByClrType);

        foreach (var modelEntity in modelEntities)
        {
            var clrType = GetClrType(modelEntity);

            if (clrType is null
                || !labelByClrType.TryGetValue(clrType, out var label)
                || (includedClrTypes is not null && !includedClrTypes.Contains(clrType)))
            {
                continue;
            }

            AppendEntityBlock(builder, label, clrType, modelEntity, entityTypeNames);
        }

        var relationshipKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var modelEntity in modelEntities)
        {
            foreach (var foreignKey in EnumerateForeignKeys(modelEntity))
            {
                var principal = GetNavigationEntityType(foreignKey, "PrincipalEntityType");
                var dependent = GetNavigationEntityType(foreignKey, "DeclaringEntityType");

                if (principal is null || dependent is null)
                {
                    continue;
                }

                var principalClr = GetClrType(principal);
                var dependentClr = GetClrType(dependent);

                if (principalClr is null
                    || dependentClr is null
                    || !labelByClrType.ContainsKey(principalClr)
                    || !labelByClrType.ContainsKey(dependentClr))
                {
                    continue;
                }

                var relationshipKey =
                    $"{principalClr.FullName}|{dependentClr.FullName}|{string.Join(',', GetForeignKeyPropertyNames(foreignKey))}";

                if (!relationshipKeys.Add(relationshipKey))
                {
                    continue;
                }

                var principalLabel = labelByClrType[principalClr];
                var dependentLabel = labelByClrType[dependentClr];
                var cardinality = GetCardinality(foreignKey);
                var relationshipLabel = EscapeMermaidLabel(GetRelationshipLabel(foreignKey));

                builder.AppendLine(
                    $"    {principalLabel} {cardinality} {dependentLabel} : \"{relationshipLabel}\"");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static HashSet<Type> CollectNeighborClrTypes(Type focalType, object? model)
    {
        var included = new HashSet<Type> { focalType };

        if (model is null)
        {
            return included;
        }

        foreach (var modelEntity in EnumerateEntityTypes(model).Where(static entity => !IsOwned(entity)))
        {
            foreach (var foreignKey in EnumerateForeignKeys(modelEntity))
            {
                var principal = GetNavigationEntityType(foreignKey, "PrincipalEntityType");
                var dependent = GetNavigationEntityType(foreignKey, "DeclaringEntityType");

                if (principal is null || dependent is null)
                {
                    continue;
                }

                var principalClr = GetClrType(principal);
                var dependentClr = GetClrType(dependent);

                if (principalClr is null || dependentClr is null)
                {
                    continue;
                }

                if (principalClr == focalType)
                {
                    included.Add(dependentClr);
                }

                if (dependentClr == focalType)
                {
                    included.Add(principalClr);
                }
            }
        }

        return included;
    }

    private static void AppendEntityBlock(
        StringBuilder builder,
        string label,
        Type entityType,
        object? modelEntity,
        HashSet<Type> entityTypeNames)
    {
        builder.AppendLine($"    {label} {{");

        var wroteField = false;

        foreach (var member in EntityDescriptor.DescribeMembers(entityType, modelEntity, entityTypeNames))
        {
            if (member.Notes.Contains("navigation", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var annotations = new List<string>();

            if (member.Notes.Contains("PK", StringComparison.Ordinal))
            {
                annotations.Add("PK");
            }

            if (member.Notes.Contains("FK", StringComparison.Ordinal))
            {
                annotations.Add("FK");
            }

            var suffix = annotations.Count == 0 ? string.Empty : $" {string.Join(' ', annotations)}";
            builder.AppendLine(
                $"        {MapMermaidType(member.TypeDisplay)} {SanitizeFieldName(member.Name)}{suffix}");
            wroteField = true;
        }

        if (!wroteField)
        {
            builder.AppendLine("        string entity");
        }

        builder.AppendLine("    }");
    }

    private static IEnumerable<object> EnumerateEntityTypes(object model)
    {
        var getEntityTypes = model.GetType()
            .GetInterfaces()
            .FirstOrDefault(static iface => string.Equals(iface.FullName, IModelFullName, StringComparison.Ordinal))
            ?.GetMethod(
                "GetEntityTypes",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);

        if (getEntityTypes?.Invoke(model, null) is not IEnumerable entityTypes)
        {
            yield break;
        }

        foreach (var entityType in entityTypes)
        {
            if (entityType is not null)
            {
                yield return entityType;
            }
        }
    }

    private static object? TryGetModel(object dbContext)
    {
        for (var type = dbContext.GetType(); type is not null; type = type.BaseType)
        {
            var modelProperty = type.GetProperty(
                "Model",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            if (modelProperty is not null)
            {
                return modelProperty.GetValue(dbContext);
            }
        }

        return null;
    }

    private static Type? GetClrType(object modelEntity)
    {
        for (var type = modelEntity.GetType(); type is not null; type = type.BaseType)
        {
            if (type.GetProperty("ClrType", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(modelEntity) is Type clrType)
            {
                return clrType;
            }
        }

        return modelEntity.GetType()
            .GetInterfaces()
            .FirstOrDefault(static iface =>
                string.Equals(iface.FullName, IEntityTypeFullName, StringComparison.Ordinal))
            ?.GetProperty("ClrType", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(modelEntity) as Type;
    }

    private static IEnumerable<object> EnumerateForeignKeys(object modelEntity)
    {
        var getForeignKeys = modelEntity.GetType()
            .GetMethod(
                "GetForeignKeys",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null)
            ?? modelEntity.GetType()
                .GetInterfaces()
                .FirstOrDefault(static iface =>
                    string.Equals(iface.FullName, IEntityTypeFullName, StringComparison.Ordinal))
                ?.GetMethod(
                    "GetForeignKeys",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);

        if (getForeignKeys?.Invoke(modelEntity, null) is not IEnumerable foreignKeys)
        {
            yield break;
        }

        foreach (var foreignKey in foreignKeys)
        {
            if (foreignKey is not null)
            {
                yield return foreignKey;
            }
        }
    }

    private static bool IsOwned(object modelEntity)
    {
        var isOwned = modelEntity.GetType()
            .GetMethod(
                "IsOwned",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null)
            ?? modelEntity.GetType()
                .GetInterfaces()
                .FirstOrDefault(static iface =>
                    string.Equals(iface.FullName, IEntityTypeFullName, StringComparison.Ordinal))
                ?.GetMethod(
                    "IsOwned",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);

        return isOwned?.Invoke(modelEntity, null) is true;
    }

    private static object? GetNavigationEntityType(object foreignKey, string propertyName)
    {
        for (var type = foreignKey.GetType(); type is not null; type = type.BaseType)
        {
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (string.Equals(property.Name, propertyName, StringComparison.Ordinal)
                    || property.Name.EndsWith($".{propertyName}", StringComparison.Ordinal))
                {
                    return property.GetValue(foreignKey);
                }
            }
        }

        return foreignKey.GetType()
            .GetInterfaces()
            .FirstOrDefault(static iface =>
                string.Equals(iface.FullName, IForeignKeyFullName, StringComparison.Ordinal))
            ?.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(foreignKey);
    }

    private static IReadOnlyList<string> GetForeignKeyPropertyNames(object foreignKey)
    {
        var getDependentProperties = foreignKey.GetType()
            .GetMethod(
                "GetDependentProperties",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null)
            ?? foreignKey.GetType()
                .GetInterfaces()
                .FirstOrDefault(static iface =>
                    string.Equals(iface.FullName, IForeignKeyFullName, StringComparison.Ordinal))
                ?.GetMethod(
                    "GetDependentProperties",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);

        if (getDependentProperties?.Invoke(foreignKey, null) is not IEnumerable properties)
        {
            return [];
        }

        return properties
            .Cast<object>()
            .Select(static property => property.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(property) as string
                ?? property.GetType()
                    .GetInterfaces()
                    .FirstOrDefault(static iface =>
                        string.Equals(iface.FullName, IPropertyFullName, StringComparison.Ordinal))
                    ?.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(property) as string)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name!)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetRelationshipLabel(object foreignKey)
    {
        foreach (var methodName in new[] { "GetReferencingSkipNavigations", "GetReferencingNavigations" })
        {
            var method = foreignKey.GetType()
                .GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null)
                ?? foreignKey.GetType()
                    .GetInterfaces()
                    .FirstOrDefault(static iface =>
                        string.Equals(iface.FullName, IForeignKeyFullName, StringComparison.Ordinal))
                    ?.GetMethod(
                        methodName,
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        Type.EmptyTypes,
                        null);

            if (method?.Invoke(foreignKey, null) is not IEnumerable navigations)
            {
                continue;
            }

            foreach (var navigation in navigations)
            {
                var name = navigation?.GetType()
                    .GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(navigation) as string;

                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
        }

        var dependentProperties = GetForeignKeyPropertyNames(foreignKey);

        return dependentProperties.Count == 0
            ? "references"
            : string.Join(", ", dependentProperties);
    }

    private static string GetCardinality(object foreignKey)
    {
        var isUnique = ReadForeignKeyBool(foreignKey, "IsUnique");
        var isRequired = ReadForeignKeyBool(foreignKey, "IsRequired");

        if (isUnique)
        {
            return isRequired ? "||--||" : "|o--||";
        }

        return isRequired ? "||--|{" : "||--o{";
    }

    private static bool ReadForeignKeyBool(object foreignKey, string propertyName)
    {
        return foreignKey.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                   ?.GetValue(foreignKey) is true
               || foreignKey.GetType()
                   .GetInterfaces()
                   .FirstOrDefault(static iface =>
                       string.Equals(iface.FullName, IForeignKeyFullName, StringComparison.Ordinal))
                   ?.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                   ?.GetValue(foreignKey) is true;
    }

    private static void ResolveLabelCollisions(Dictionary<Type, string> labelByClrType)
    {
        var groups = labelByClrType
            .GroupBy(static entry => entry.Value, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1);

        foreach (var group in groups)
        {
            foreach (var entry in group)
            {
                var suffix = entry.Key.Namespace?.Split('.').LastOrDefault();

                labelByClrType[entry.Key] = string.IsNullOrWhiteSpace(suffix)
                    ? SanitizeEntityName(entry.Key.FullName ?? entry.Key.Name)
                    : SanitizeEntityName($"{entry.Key.Name}_{suffix}");
            }
        }
    }

    private static string SanitizeEntityName(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        var sanitized = builder.ToString();

        return string.IsNullOrWhiteSpace(sanitized) ? "Entity" : sanitized;
    }

    private static string SanitizeFieldName(string name)
    {
        var sanitized = SanitizeEntityName(name);
        return char.IsDigit(sanitized[0]) ? $"_{sanitized}" : sanitized;
    }

    private static string MapMermaidType(string typeDisplay)
    {
        return typeDisplay switch
        {
            "string" => "string",
            "int" => "int",
            "long" => "long",
            "bool" => "bool",
            "decimal" => "decimal",
            "DateTime" => "datetime",
            "DateTimeOffset" => "datetime",
            "Guid" => "string",
            _ when typeDisplay.EndsWith("[]", StringComparison.Ordinal) => "string",
            _ when typeDisplay.EndsWith('?') => MapMermaidType(typeDisplay[..^1]),
            _ => "string"
        };
    }

    private static string EscapeMermaidLabel(string label)
    {
        return label.Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
