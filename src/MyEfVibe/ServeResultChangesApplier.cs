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
            ClearChangeTracker(dbContext: runtime.DbContext);

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
                GetFailureMessage(failure));
        }
    }

    private static string GetFailureMessage(Exception failure)
    {
        return failure is TargetInvocationException { InnerException: { } inner }
            ? inner.Message
            : failure.Message;
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

    private static void ClearChangeTracker(object dbContext)
    {
        try
        {
            var changeTracker = dbContext.GetType()
                .GetProperty("ChangeTracker", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(dbContext);

            if (changeTracker is null)
            {
                return;
            }

            var clearMethod = changeTracker.GetType()
                .GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);

            if (clearMethod is not null)
            {
                clearMethod.Invoke(changeTracker, null);
                return;
            }

            var entriesMethod = changeTracker.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(static method =>
                    string.Equals(method.Name, "Entries", StringComparison.Ordinal)
                    && !method.IsGenericMethodDefinition
                    && method.GetParameters().Length == 0);

            if (entriesMethod?.Invoke(changeTracker, null) is not IEnumerable entries)
            {
                return;
            }

            foreach (var entry in entries.Cast<object>().ToArray())
            {
                var stateProperty = entry.GetType()
                    .GetProperty("State", BindingFlags.Public | BindingFlags.Instance);

                if (stateProperty?.PropertyType.IsEnum is not true)
                {
                    continue;
                }

                stateProperty.SetValue(
                    entry,
                    Enum.Parse(stateProperty.PropertyType, "Detached"));
            }
        }
        catch
        {
            // Best effort cleanup: preserve the original apply failure reported to the client.
        }
    }

    private static List<string> GetPrimaryKeyNames(Type entityType, object? modelEntity)
    {
        if (modelEntity is not null)
        {
            var keys = EnumerateModelPrimaryKeyNames(modelEntity)
                .ToList();

            if (keys.Count > 0)
            {
                return keys;
            }

            keys = EnumerateModelProperties(modelEntity)
                .Where(static property => property.IsPrimaryKey)
                .Select(static property => property.Name)
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

    private static IEnumerable<string> EnumerateModelPrimaryKeyNames(object modelEntity)
    {
        var primaryKey = InvokeParameterlessMethod(modelEntity, "FindPrimaryKey");
        var properties = primaryKey is null
            ? null
            : ReadPropertyValue(primaryKey, "Properties") as IEnumerable;

        if (properties is null)
        {
            yield break;
        }

        foreach (var property in properties)
        {
            var name = property is null ? null : ReadPropertyValue(property, "Name") as string;

            if (!string.IsNullOrEmpty(name))
            {
                yield return name;
            }
        }
    }

    private static object? InvokeParameterlessMethod(object target, string methodName)
    {
        var method = target.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method =>
                IsMemberNameMatch(method.Name, methodName)
                && method.GetParameters().Length == 0);

        if (method is not null)
        {
            return method.Invoke(target, null);
        }

        foreach (var interfaceType in target.GetType().GetInterfaces())
        {
            method = interfaceType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            if (method is not null && method.GetParameters().Length == 0)
            {
                return method.Invoke(target, null);
            }
        }

        return null;
    }

    private static object? ReadPropertyValue(object target, string propertyName)
    {
        var property = target.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(property => IsMemberNameMatch(property.Name, propertyName));

        if (property is not null)
        {
            return property.GetValue(target);
        }

        foreach (var interfaceType in target.GetType().GetInterfaces())
        {
            property = interfaceType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property is not null)
            {
                return property.GetValue(target);
            }
        }

        return null;
    }

    private static bool IsMemberNameMatch(string actualName, string expectedName)
    {
        return string.Equals(actualName, expectedName, StringComparison.Ordinal)
               || actualName.EndsWith($".{expectedName}", StringComparison.Ordinal);
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
            .Select(name =>
            {
                if (!keys.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException(
                        $"Missing primary key `{name}` for result persistence.");
                }

                return ConvertStringToPropertyValue(
                    value,
                    entityType.GetProperty(name)?.PropertyType ?? typeof(string));
            })
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
