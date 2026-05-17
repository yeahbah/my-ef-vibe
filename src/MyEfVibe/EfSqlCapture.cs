using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace MyEfVibe;

internal sealed class EfSqlCapture : IDisposable
{
    private readonly List<SqlCommandEntry> _entries = new();
    private readonly Stopwatch _sessionStopwatch = Stopwatch.StartNew();
    private long _lastCommandStartedMs;

    private EfSqlCapture()
    {
    }

    internal static EfSqlCapture? TryAttach(object dbContextInstance)
    {
        var databaseFacade = dbContextInstance.GetType().GetProperty("Database")?.GetValue(dbContextInstance);

        if (databaseFacade is null)
            return null;

        if (!TryLocateLogToMethod(databaseFacade, out var logToMethod, out var logLevelValue, out var commandCategory))
            return null;

        var capture = new EfSqlCapture();

        Action<string> logAction = capture.OnLog;

        if (!TryInvokeLogTo(logToMethod, databaseFacade, logAction, logLevelValue, commandCategory))
            return null;

        return capture;
    }

    internal bool HasEntries => _entries.Count > 0;

    internal IReadOnlyList<SqlCommandEntry> Commands => _entries;

    internal IReadOnlyList<string> Entries =>
        _entries.Select(static entry => entry.Text).ToArray();

    internal long TotalDatabaseMilliseconds =>
        _entries.Sum(static entry => entry.DurationMilliseconds ?? 0);

    internal void WriteCapturedSql(TextWriter writer)
    {
        if (_entries.Count == 0)
            return;

        writer.WriteLine("Executed SQL:");

        foreach (var entry in _entries)
            writer.WriteLine(FormatEntry(entry));
    }

    internal static string FormatEntry(SqlCommandEntry entry)
    {
        if (entry.Parameters.Count == 0 && entry.DurationMilliseconds is null)
            return entry.Text;

        var builder = new StringBuilder();
        builder.Append(entry.Text);

        if (entry.Parameters.Count > 0)
        {
            builder.AppendLine();
            builder.Append("  -- parameters: ");
            builder.Append(string.Join(", ", entry.Parameters));
        }

        if (entry.DurationMilliseconds is not null)
        {
            builder.AppendLine();
            builder.Append($"  -- duration: {entry.DurationMilliseconds} ms");
        }

        return builder.ToString();
    }

    public void Dispose()
    {
        _entries.Clear();
    }

    private void OnLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (!LooksLikeDatabaseCommandLog(message))
            return;

        var normalized = NormalizeLogMessage(message);

        if (string.IsNullOrWhiteSpace(normalized))
            return;

        if (_entries.Count > 0 && string.Equals(_entries[^1].Text, normalized, StringComparison.Ordinal))
            return;

        var nowMs = _sessionStopwatch.ElapsedMilliseconds;
        var duration = _entries.Count == 0 ? null : (long?)(nowMs - _lastCommandStartedMs);

        if (_entries.Count > 0 && duration is < 0)
            duration = null;

        _entries.Add(new SqlCommandEntry(normalized, duration, ExtractParameters(normalized)));
        _lastCommandStartedMs = nowMs;
    }

    private static IReadOnlyList<string> ExtractParameters(string message)
    {
        var parameters = new List<string>();

        foreach (var line in message.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("@", StringComparison.Ordinal) || line.Contains("p__", StringComparison.Ordinal))
                parameters.Add(line);
        }

        return parameters;
    }

    private static bool LooksLikeDatabaseCommandLog(string message)
    {
        if (message.Contains("DbCommand", StringComparison.OrdinalIgnoreCase))
            return true;

        if (message.Contains("CommandType=", StringComparison.OrdinalIgnoreCase))
            return true;

        return ContainsSqlKeyword(message);
    }

    private static bool ContainsSqlKeyword(string message)
    {
        foreach (var keyword in new[] { "SELECT ", "INSERT ", "UPDATE ", "DELETE ", "FROM ", "WITH " })
        {
            if (message.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool TryInvokeLogTo(
        MethodInfo logToMethod,
        object databaseFacade,
        Action<string> logAction,
        object logLevelValue,
        string commandCategory)
    {
        var parameters = logToMethod.GetParameters();

        try
        {
            if (parameters.Length == 2)
            {
                logToMethod.Invoke(null, new object?[] { databaseFacade, logAction });

                return true;
            }

            if (parameters.Length == 3)
            {
                logToMethod.Invoke(null, new object?[] { databaseFacade, logAction, logLevelValue });

                return true;
            }

            logToMethod.Invoke(null, new object?[] { databaseFacade, logAction, logLevelValue, new[] { commandCategory } });

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeLogMessage(string message)
    {
        var lines = message
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length == 0)
            return message.Trim();

        var builder = new StringBuilder();

        foreach (var line in lines)
        {
            if (builder.Length > 0)
                builder.AppendLine();

            builder.Append(line);
        }

        return builder.ToString();
    }

    private static bool TryLocateLogToMethod(
        object databaseFacade,
        out MethodInfo logToMethod,
        out object logLevelValue,
        out string commandCategory)
    {
        logToMethod = null!;
        logLevelValue = 2;
        commandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.FullName?.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) != true)
                continue;

            foreach (var exported in ReflectionToolkit.EnumerateLoadableExportedTypes(assembly))
            foreach (var candidate in exported.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!candidate.IsDefined(typeof(ExtensionAttribute), inherit: false))
                    continue;

                if (!string.Equals(candidate.Name, "LogTo", StringComparison.Ordinal))
                    continue;

                var parameters = candidate.GetParameters();

                if (parameters.Length < 2)
                    continue;

                if (!parameters[0].ParameterType.IsAssignableFrom(databaseFacade.GetType()))
                    continue;

                if (parameters[1].ParameterType == typeof(Action<string>))
                {
                    logToMethod = candidate;

                    if (parameters.Length >= 3 && parameters[2].ParameterType.IsEnum)
                        logLevelValue = Enum.ToObject(parameters[2].ParameterType, 2);

                    return true;
                }

                if (parameters[1].ParameterType.IsGenericType
                    && parameters[1].ParameterType.GetGenericTypeDefinition() == typeof(Action<>))
                {
                    var actionGenericArgs = parameters[1].ParameterType.GetGenericArguments();

                    if (actionGenericArgs.Length == 4
                        && actionGenericArgs[3] == typeof(string))
                    {
                        logToMethod = candidate;

                        if (parameters.Length >= 3 && parameters[2].ParameterType.IsEnum)
                            logLevelValue = Enum.ToObject(parameters[2].ParameterType, 2);

                        return true;
                    }
                }
            }
        }

        return false;
    }
}
