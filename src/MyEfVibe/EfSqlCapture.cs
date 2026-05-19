using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace MyEfVibe;

internal sealed class EfSqlCapture : IDisposable
{
    private readonly List<SqlCommandEntry> _entries = new();
    private readonly Stopwatch _sessionStopwatch = Stopwatch.StartNew();
    private IDisposable? _logSubscription;
    private long _lastCommandStartedMs;

    private EfSqlCapture()
    {
    }

    internal static EfSqlCapture? TryAttach(object dbContextInstance)
    {
        var databaseFacade = dbContextInstance.GetType().GetProperty("Database")?.GetValue(dbContextInstance);

        if (databaseFacade is null)
            return null;

        var binding = EntityFrameworkReflectionCache.ResolveLogTo(databaseFacade);

        if (binding is null)
            return null;

        var capture = new EfSqlCapture();

        if (!TryInvokeLogTo(binding, databaseFacade, capture.OnLog, capture.OnDbCommand, out capture._logSubscription))
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
        _logSubscription?.Dispose();
        _entries.Clear();
    }

    private void OnLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (!LooksLikeDatabaseCommandLog(message))
            return;

        RecordCommand(NormalizeLogMessage(message));
    }

    private void OnDbCommand(object dbCommand)
    {
        var commandText = dbCommand.GetType().GetProperty("CommandText")?.GetValue(dbCommand) as string;

        if (string.IsNullOrWhiteSpace(commandText))
            return;

        RecordCommand(commandText.Trim());
    }

    private void RecordCommand(string normalized)
    {
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
        LogToBinding binding,
        object databaseFacade,
        Action<string> stringLogAction,
        Action<object> commandLogAction,
        out IDisposable? subscription)
    {
        subscription = null;
        var parameters = binding.Method.GetParameters();

        try
        {
            var logAction = ResolveLogAction(binding.Method, stringLogAction, commandLogAction);

            if (logAction is null)
                return false;

            if (parameters.Length == 2)
            {
                subscription = binding.Method.Invoke(null, new object?[] { databaseFacade, logAction }) as IDisposable;

                return subscription is not null;
            }

            if (parameters.Length == 3)
            {
                subscription = binding.Method.Invoke(
                    null,
                    new object?[] { databaseFacade, logAction, binding.LogLevelValue }) as IDisposable;

                return subscription is not null;
            }

            subscription = binding.Method.Invoke(
                null,
                new object?[]
                {
                    databaseFacade,
                    logAction,
                    binding.LogLevelValue,
                    new[] { binding.CommandCategory },
                }) as IDisposable;

            return subscription is not null;
        }
        catch
        {
            return false;
        }
    }

    private static object? ResolveLogAction(
        MethodInfo logToMethod,
        Action<string> stringLogAction,
        Action<object> commandLogAction)
    {
        var actionType = logToMethod.GetParameters()[1].ParameterType;

        if (actionType == typeof(Action<string>))
            return stringLogAction;

        if (!actionType.IsGenericType || actionType.GetGenericTypeDefinition() != typeof(Action<>))
            return null;

        var argumentType = actionType.GetGenericArguments()[0];

        if (argumentType == typeof(string))
            return stringLogAction;

        if (!string.Equals(argumentType.Name, "DbCommand", StringComparison.Ordinal))
            return null;

        return commandLogAction;
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

            var trimmed = line.Trim();

            if (ContainsSqlKeyword(trimmed) || trimmed.StartsWith("@", StringComparison.Ordinal))
            {
                builder.Append(trimmed);
                continue;
            }

            if (builder.Length == 0 && !LooksLikeDatabaseCommandLog(trimmed))
                continue;

            builder.Append(trimmed);
        }

        var normalized = builder.ToString().Trim();

        if (ContainsSqlKeyword(normalized))
            return normalized;

        foreach (var line in lines)
        {
            if (ContainsSqlKeyword(line))
                return line.Trim();
        }

        return normalized;
    }
}
