using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace MyEfVibe;

internal sealed record DiagnosticSqlCaptureBinding(
    string CommandExecutedEventName,
    Type CommandExecutedEventDataType,
    PropertyInfo ContextProperty,
    PropertyInfo CommandProperty,
    PropertyInfo DurationProperty,
    PropertyInfo LogLevelProperty,
    PropertyInfo? LogCommandTextProperty);

internal static class DiagnosticSqlCaptureResolver
{
    private static readonly ConcurrentDictionary<string, DiagnosticSqlCaptureBinding?> Bindings = new(StringComparer.Ordinal);

    internal static DiagnosticSqlCaptureBinding? Resolve(object dbContextInstance)
    {
        var key = ResolveRelationalAssembly(dbContextInstance)?.FullName ?? dbContextInstance.GetType().Assembly.FullName!;

        return Bindings.GetOrAdd(key, _ => LocateBinding(dbContextInstance));
    }

    internal static DiagnosticListener? ResolveDiagnosticListener(object dbContextInstance)
    {
        var serviceProvider = GetInfrastructureServiceProvider(dbContextInstance);

        if (serviceProvider is null)
            return null;

        try
        {
            var diagnosticSource = serviceProvider.GetService(typeof(DiagnosticSource));

            return diagnosticSource as DiagnosticListener;
        }
        catch
        {
            return null;
        }
    }

    private static Assembly? ResolveRelationalAssembly(object dbContextInstance)
    {
        foreach (var assembly in OrderAssemblies(dbContextInstance))
        {
            if (assembly.GetType(
                    "Microsoft.EntityFrameworkCore.Diagnostics.CommandExecutedEventData",
                    throwOnError: false)
                is not null)
                return assembly;
        }

        return null;
    }

    private static IEnumerable<Assembly> OrderAssemblies(object dbContextInstance)
    {
        var seen = new HashSet<Assembly>();

        yield return dbContextInstance.GetType().Assembly;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (seen.Add(assembly))
                yield return assembly;
        }
    }

    private static DiagnosticSqlCaptureBinding? LocateBinding(object dbContextInstance)
    {
        var relationalAssembly = ResolveRelationalAssembly(dbContextInstance);

        if (relationalAssembly is null)
            return null;

        var eventDataType = relationalAssembly.GetType(
            "Microsoft.EntityFrameworkCore.Diagnostics.CommandExecutedEventData",
            throwOnError: false);

        if (eventDataType is null)
            return null;

        var relationalEventIdType = relationalAssembly.GetType(
            "Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId",
            throwOnError: false);

        var commandExecutedEventName = ResolveCommandExecutedEventName(relationalEventIdType);

        if (commandExecutedEventName is null)
            return null;

        var contextProperty = eventDataType.GetProperty("Context");

        var commandProperty = eventDataType.GetProperty("Command");

        var durationProperty = eventDataType.GetProperty("Duration");

        var logLevelProperty = eventDataType.GetProperty("LogLevel");

        if (contextProperty is null || commandProperty is null || durationProperty is null || logLevelProperty is null)
            return null;

        return new DiagnosticSqlCaptureBinding(
            commandExecutedEventName,
            eventDataType,
            contextProperty,
            commandProperty,
            durationProperty,
            logLevelProperty,
            eventDataType.GetProperty("LogCommandText"));
    }

    private static string? ResolveCommandExecutedEventName(Type? relationalEventIdType)
    {
        if (relationalEventIdType is null)
            return null;

        try
        {
            var commandExecutedField = relationalEventIdType.GetField(
                "CommandExecuted",
                BindingFlags.Public | BindingFlags.Static);

            if (commandExecutedField is null)
                return null;

            var eventId = commandExecutedField.GetValue(null);

            if (eventId is null)
                return null;

            return eventId.GetType().GetProperty("Name")?.GetValue(eventId) as string;
        }
        catch
        {
            return null;
        }
    }

    private static IServiceProvider? GetInfrastructureServiceProvider(object dbContextInstance)
    {
        foreach (var iface in dbContextInstance.GetType().GetInterfaces())
        {
            if (!iface.IsGenericType)
                continue;

            if (!string.Equals(
                    iface.GetGenericTypeDefinition().FullName,
                    "Microsoft.EntityFrameworkCore.Infrastructure.IInfrastructure`1",
                    StringComparison.Ordinal))
                continue;

            if (iface.GetGenericArguments()[0] != typeof(IServiceProvider))
                continue;

            return iface.GetProperty("Instance")?.GetValue(dbContextInstance) as IServiceProvider;
        }

        return null;
    }
}
