using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyEfVibe.Reporters;
using MyEfVibe.Workspace;

namespace MyEfVibe;

internal static class ServeResultChangesApplier
{
    internal static async Task ApplyAndWriteJsonAsync(
        WorkspaceRuntime runtime,
        string entityName,
        IReadOnlyList<ServeResultChangeRequest> updates,
        IReadOnlyList<ServeResultChangeRequest> deletes,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var applied = await ApplyAsync(
                runtime.DbContext,
                entityName,
                updates,
                deletes,
                cancellationToken);

            stopwatch.Stop();

            EvaluationJsonReporter.WriteSuccess(
                applied,
                new EvaluationMetrics
                {
                    Snippet = $"applyResultChanges:{entityName}",
                    TotalMilliseconds = stopwatch.ElapsedMilliseconds,
                    SqlCommandCount = 1,
                    ResultKind = ResultKind.Scalar,
                    ResultTypeName = "apply-result-changes",
                    Succeeded = true,
                    Warnings = []
                });
        }
        catch (Exception failure)
        {
            stopwatch.Stop();

            EvaluationJsonReporter.WriteFailure(
                new EvaluationMetrics
                {
                    Snippet = $"applyResultChanges:{entityName}",
                    TotalMilliseconds = stopwatch.ElapsedMilliseconds,
                    SqlCommandCount = 0,
                    ResultKind = ResultKind.Object,
                    ResultTypeName = "apply-result-changes",
                    Succeeded = false,
                    Warnings = []
                },
                failure.Message);
        }
    }

    private static async Task<string> ApplyAsync(
        object dbContext,
        string entityName,
        IReadOnlyList<ServeResultChangeRequest> updates,
        IReadOnlyList<ServeResultChangeRequest> deletes,
        CancellationToken cancellationToken)
    {
        var dbSets = EntityDescriptor.EnumerateDbSetEntities(dbContext).ToArray();

        switch (EntityDescriptor.TryResolveEntity(dbSets, entityName.Trim(), out var resolved))
        {
            case EntityDescriptor.EntityResolveResult.NotFound:
                throw new InvalidOperationException($"Entity `{entityName}` was not found.");

            case EntityDescriptor.EntityResolveResult.Ambiguous:
                throw new InvalidOperationException(
                    $"Multiple entities match `{entityName}`. Use a DbSet name or full type name.");

            case EntityDescriptor.EntityResolveResult.Found:
                break;

            default:
                throw new InvalidOperationException("Could not resolve entity.");
        }

        var match = resolved.Match!.Value;
        var entityType = match.EntityType;
        var modelEntity = EntityDescriptor.TryFindModelEntity(dbContext, entityType);
        var primaryKeys = GetPrimaryKeyNames(entityType, modelEntity);

        if (primaryKeys.Count == 0)
        {
            throw new InvalidOperationException(
                $"Entity `{match.DbSetName}` does not expose a primary key for result persistence.");
        }

        var dbSet = dbContext.GetType()
                         .GetProperty(
                             match.DbSetName,
                             BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                         ?.GetValue(dbContext)
                    ?? throw new InvalidOperationException($"DbSet `{match.DbSetName}` is not available.");

        var updatedCount = 0;
        var deletedCount = 0;

        foreach (var delete in deletes)
        {
            var entity = FindEntity(dbSet, entityType, primaryKeys, delete.Keys)
                         ?? throw new InvalidOperationException(
                             $"Could not find `{match.DbSetName}` row to delete ({FormatKeys(delete.Keys, primaryKeys)}).");

            InvokeRemove(dbContext, entity);
            deletedCount++;
        }

        foreach (var update in updates)
        {
            var entity = FindEntity(dbSet, entityType, primaryKeys, update.Keys)
                         ?? throw new InvalidOperationException(
                             $"Could not find `{match.DbSetName}` row to update ({FormatKeys(update.Keys, primaryKeys)}).");

            ApplyValues(entity, entityType, update.Values ?? new Dictionary<string, string>());
            updatedCount++;
        }

        if (updatedCount == 0 && deletedCount == 0)
        {
            return "No changes applied.";
        }

        await InvokeSaveChangesAsync(dbContext, cancellationToken);

        return $"{updatedCount} row(s) updated, {deletedCount} row(s) deleted.";
    }

    private static List<string> GetPrimaryKeyNames(Type entityType, object? modelEntity)
    {
        if (modelEntity is not null)
        {
            var keys = EnumerateModelProperties(modelEntity)
                .Where(static property => property.IsPrimaryKey)
                .Select(static property => property.Name)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToList();

            if (keys.Count > 0)
            {
                return keys;
            }
        }

        return entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
            .Select(static property => property.Name)
            .ToList();
    }

    private static IEnumerable<ModelPropertySnapshot> EnumerateModelProperties(object modelEntity)
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

            yield return new ModelPropertySnapshot(
                name,
                ReadBool(property, "IsPrimaryKey"));
        }
    }

    private static bool ReadBool(object target, string propertyName)
    {
        return target.GetType().GetProperty(propertyName)?.GetValue(target) is true;
    }

    private sealed record ModelPropertySnapshot(string Name, bool IsPrimaryKey);

    private static object? FindEntity(
        object dbSet,
        Type entityType,
        IReadOnlyList<string> primaryKeys,
        IReadOnlyDictionary<string, string> keys)
    {
        var keyValues = primaryKeys
            .Select(name => ConvertStringToPropertyValue(
                keys.TryGetValue(name, out var value) ? value : string.Empty,
                entityType.GetProperty(name)?.PropertyType ?? typeof(string)))
            .ToArray();

        var findMethod = dbSet.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method =>
                string.Equals(method.Name, "Find", StringComparison.Ordinal)
                && method.GetParameters().Length == 1);

        if (findMethod is null)
        {
            throw new InvalidOperationException("DbSet.Find is not available on this context.");
        }

        var parameterType = findMethod.GetParameters()[0].ParameterType;

        return parameterType == typeof(object[])
            ? findMethod.Invoke(dbSet, [keyValues])
            : findMethod.Invoke(dbSet, keyValues);
    }

    private static void ApplyValues(
        object entity,
        Type entityType,
        IReadOnlyDictionary<string, string> values)
    {
        foreach (var (name, rawValue) in values)
        {
            var property = entityType.GetProperty(
                name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property is null || !property.CanWrite || IsNavigationProperty(property))
            {
                continue;
            }

            property.SetValue(entity, ConvertStringToPropertyValue(rawValue, property.PropertyType));
        }
    }

    private static bool IsNavigationProperty(PropertyInfo property)
    {
        var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (propertyType == typeof(string))
        {
            return false;
        }

        if (propertyType.IsPrimitive || propertyType.IsEnum || propertyType == typeof(decimal)
            || propertyType == typeof(DateTime) || propertyType == typeof(DateTimeOffset)
            || propertyType == typeof(Guid) || propertyType == typeof(TimeSpan))
        {
            return false;
        }

        return propertyType.IsClass
               || typeof(IEnumerable).IsAssignableFrom(propertyType);
    }

    private static object? ConvertStringToPropertyValue(string value, Type propertyType)
    {
        var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (string.IsNullOrEmpty(value))
        {
            if (Nullable.GetUnderlyingType(propertyType) is not null)
            {
                return null;
            }

            return underlying.IsValueType ? Activator.CreateInstance(underlying) : null;
        }

        if (underlying == typeof(string))
        {
            return value;
        }

        if (underlying == typeof(bool))
        {
            return bool.Parse(value);
        }

        if (underlying == typeof(int))
        {
            return int.Parse(value, CultureInfo.InvariantCulture);
        }

        if (underlying == typeof(long))
        {
            return long.Parse(value, CultureInfo.InvariantCulture);
        }

        if (underlying == typeof(decimal))
        {
            return decimal.Parse(value, CultureInfo.InvariantCulture);
        }

        if (underlying == typeof(double))
        {
            return double.Parse(value, CultureInfo.InvariantCulture);
        }

        if (underlying == typeof(float))
        {
            return float.Parse(value, CultureInfo.InvariantCulture);
        }

        if (underlying == typeof(Guid))
        {
            return Guid.Parse(value);
        }

        if (underlying == typeof(DateTime))
        {
            return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        if (underlying == typeof(DateTimeOffset))
        {
            return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        if (underlying.IsEnum)
        {
            return Enum.Parse(underlying, value, ignoreCase: true);
        }

        return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
    }

    private static void InvokeRemove(object dbContext, object entity)
    {
        var removeMethod = dbContext.GetType()
            .GetMethod("Remove", [typeof(object)])
            ?? throw new InvalidOperationException("DbContext.Remove is not available.");

        removeMethod.Invoke(dbContext, [entity]);
    }

    private static async Task InvokeSaveChangesAsync(object dbContext, CancellationToken cancellationToken)
    {
        var saveMethod = dbContext.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method =>
                string.Equals(method.Name, "SaveChangesAsync", StringComparison.Ordinal)
                && method.GetParameters().Length <= 1);

        if (saveMethod is null)
        {
            throw new InvalidOperationException("DbContext.SaveChangesAsync is not available.");
        }

        var result = saveMethod.GetParameters().Length == 0
            ? saveMethod.Invoke(dbContext, null)
            : saveMethod.Invoke(dbContext, [cancellationToken]);

        if (result is Task task)
        {
            await task.ConfigureAwait(false);
        }
    }

    private static string FormatKeys(IReadOnlyDictionary<string, string> keys, IReadOnlyList<string> primaryKeys)
    {
        return string.Join(", ", primaryKeys.Select(name => $"{name}={keys.GetValueOrDefault(name)}"));
    }

    internal sealed class ServeResultChangeRequest
    {
        public Dictionary<string, string> Keys { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string>? Values { get; init; }
    }
}

internal sealed class ApplyResultChangesJsonReporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    internal static IReadOnlyList<ServeResultChangesApplier.ServeResultChangeRequest> ParseChanges(
        JsonElement? element)
    {
        if (element is null or { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined })
        {
            return [];
        }

        return JsonSerializer.Deserialize<ServeResultChangesApplier.ServeResultChangeRequest[]>(
                   element.Value.GetRawText(),
                   SerializerOptions)
               ?? [];
    }
}
