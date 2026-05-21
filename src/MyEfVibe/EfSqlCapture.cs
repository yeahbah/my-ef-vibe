using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace MyEfVibe;

internal sealed class EfSqlCapture : IDisposable
{
    private readonly List<SqlCommandEntry> _entries = new();
    private readonly object _dbContextInstance;
    private readonly DbLogSettings _settings;
    private readonly DiagnosticSqlCaptureBinding? _diagnosticBinding;
    private IDisposable? _logSubscription;
    private readonly List<IDisposable> _listenerSubscriptions = new();

    private EfSqlCapture(object dbContextInstance, DbLogSettings settings, DiagnosticSqlCaptureBinding? diagnosticBinding)
    {
        _dbContextInstance = dbContextInstance;
        _settings = settings;
        _diagnosticBinding = diagnosticBinding;
    }

    internal static EfSqlCapture? TryAttach(object dbContextInstance, DbLogSettings settings)
    {
        if (!settings.Enabled)
            return null;

        var diagnosticBinding = DiagnosticSqlCaptureResolver.Resolve(dbContextInstance);
        var capture = new EfSqlCapture(dbContextInstance, settings, diagnosticBinding);

        if (diagnosticBinding is not null
            && TrySubscribeDiagnostics(capture, diagnosticBinding, dbContextInstance))
            return capture;

        var databaseFacade = dbContextInstance.GetType().GetProperty("Database")?.GetValue(dbContextInstance);

        if (databaseFacade is null)
            return null;

        var logToBinding = EntityFrameworkReflectionCache.ResolveLogTo(databaseFacade);

        if (logToBinding is null)
            return null;

        if (!TryInvokeLogTo(
                logToBinding,
                databaseFacade,
                settings,
                capture.OnLog,
                capture.OnDbCommand,
                out capture._logSubscription))
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

        foreach (var subscription in _listenerSubscriptions)
            subscription.Dispose();

        _listenerSubscriptions.Clear();
        _entries.Clear();
    }

    private static bool TrySubscribeDiagnostics(
        EfSqlCapture capture,
        DiagnosticSqlCaptureBinding binding,
        object dbContextInstance)
    {
        var diagnosticListener = DiagnosticSqlCaptureResolver.ResolveDiagnosticListener(dbContextInstance);

        if (diagnosticListener is not null)
        {
            capture._listenerSubscriptions.Add(
                diagnosticListener.Subscribe(new DiagnosticEventObserver(capture, binding, capture._settings.Verbose)));

            return true;
        }

        capture._listenerSubscriptions.Add(
            DiagnosticListener.AllListeners.Subscribe(new DiagnosticListenerObserver(capture, binding, capture._settings.Verbose)));

        return true;
    }

    private void OnCommandExecutedDiagnosticEvent(object eventData)
    {
        if (_diagnosticBinding is null)
            return;

        try
        {
            if (!TryAcceptDiagnosticEvent(eventData, out var durationMs, out var command))
                return;

            var commandText = ExtractCommandText(_diagnosticBinding, eventData, command);

            if (string.IsNullOrWhiteSpace(commandText))
                return;

            RecordCommand(commandText.Trim(), durationMs, ExtractDbCommandParameters(command));
        }
        catch
        {
            // Ignore malformed diagnostic payloads from other providers.
        }
    }

    private void OnVerboseDiagnosticEvent(string eventName, object eventData)
    {
        try
        {
            if (!TryAcceptDiagnosticEvent(eventData, out var durationMs, out var command))
                return;

            var message = FormatVerboseDiagnosticMessage(eventName, eventData, command);

            if (string.IsNullOrWhiteSpace(message))
                return;

            RecordCommand(
                message,
                durationMs,
                command is null ? Array.Empty<string>() : ExtractDbCommandParameters(command));
        }
        catch
        {
            // Ignore malformed diagnostic payloads from other providers.
        }
    }

    private bool TryAcceptDiagnosticEvent(object eventData, out long? durationMs, out object? command)
    {
        durationMs = null;
        command = null;

        var eventType = eventData.GetType();
        var contextProperty = eventType.GetProperty("Context")
            ?? _diagnosticBinding?.ContextProperty;

        var logLevelProperty = eventType.GetProperty("LogLevel")
            ?? _diagnosticBinding?.LogLevelProperty;

        if (contextProperty?.GetValue(eventData) is not { } context
            || !IsTargetDbContext(context))
            return false;

        if (logLevelProperty?.GetValue(eventData) is Enum eventLogLevel
            && (int)(object)eventLogLevel < (int)_settings.Level)
            return false;

        var durationProperty = eventType.GetProperty("Duration") ?? _diagnosticBinding?.DurationProperty;
        durationMs = ExtractDurationMilliseconds(durationProperty?.GetValue(eventData));

        var commandProperty = eventType.GetProperty("Command") ?? _diagnosticBinding?.CommandProperty;
        command = commandProperty?.GetValue(eventData);

        return true;
    }

    private string FormatVerboseDiagnosticMessage(string eventName, object eventData, object? command)
    {
        var eventType = eventData.GetType();
        var logLevel = (eventType.GetProperty("LogLevel") ?? _diagnosticBinding?.LogLevelProperty)
            ?.GetValue(eventData) as Enum;

        var logCommandText = (eventType.GetProperty("LogCommandText") ?? _diagnosticBinding?.LogCommandTextProperty)
            ?.GetValue(eventData) as string;

        var builder = new StringBuilder();
        builder.Append('[');
        builder.Append(logLevel?.ToString()?.ToLowerInvariant() ?? "information");
        builder.Append("] ");
        builder.Append(eventName);

        if (!string.IsNullOrWhiteSpace(logCommandText))
        {
            builder.AppendLine();
            builder.Append(logCommandText.Trim());
        }
        else if (command is not null)
        {
            var commandText = command.GetType().GetProperty("CommandText")?.GetValue(command) as string;

            if (!string.IsNullOrWhiteSpace(commandText))
            {
                builder.AppendLine();
                builder.Append(commandText.Trim());
            }
        }

        return builder.ToString().Trim();
    }

    private void OnLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (!_settings.Verbose && !LooksLikeDatabaseCommandLog(message))
            return;

        var normalized = _settings.Verbose ? message.Trim() : NormalizeLogMessage(message);
        RecordCommand(normalized, null, ExtractParameters(normalized));
    }

    private void OnDbCommand(object dbCommand)
    {
        var commandText = dbCommand.GetType().GetProperty("CommandText")?.GetValue(dbCommand) as string;

        if (string.IsNullOrWhiteSpace(commandText))
            return;

        RecordCommand(commandText.Trim(), null, ExtractDbCommandParameters(dbCommand));
    }

    private bool IsTargetDbContext(object context) =>
        ReferenceEquals(context, _dbContextInstance) || Equals(context, _dbContextInstance);

    private void RecordCommand(string normalized, long? durationMilliseconds, IReadOnlyList<string> parameters)
    {
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        if (_entries.Count > 0 && string.Equals(_entries[^1].Text, normalized, StringComparison.Ordinal))
            return;

        _entries.Add(new SqlCommandEntry(normalized, durationMilliseconds, parameters));
    }

    private static long? ExtractDurationMilliseconds(object? durationValue)
    {
        if (durationValue is TimeSpan duration)
            return (long)Math.Round(duration.TotalMilliseconds);

        return null;
    }

    private static string? ExtractCommandText(
        DiagnosticSqlCaptureBinding binding,
        object eventData,
        object? command)
    {
        if (binding.LogCommandTextProperty?.GetValue(eventData) is string logCommandText
            && !string.IsNullOrWhiteSpace(logCommandText))
            return logCommandText;

        if (command is null)
            return null;

        return command.GetType().GetProperty("CommandText")?.GetValue(command) as string;
    }

    private static IReadOnlyList<string> ExtractDbCommandParameters(object? command)
    {
        if (command is null)
            return Array.Empty<string>();

        if (command is DbCommand dbCommand)
            return ExtractDbCommandParameters(dbCommand);

        var parametersObject = command.GetType().GetProperty("Parameters")?.GetValue(command);

        if (parametersObject is not System.Collections.IEnumerable parameters)
            return Array.Empty<string>();

        var values = new List<string>();

        foreach (var parameter in parameters)
        {
            if (parameter is DbParameter dbParameter)
            {
                values.Add(FormatDbParameter(dbParameter));
                continue;
            }

            var name = parameter.GetType().GetProperty("ParameterName")?.GetValue(parameter) as string;
            var value = parameter.GetType().GetProperty("Value")?.GetValue(parameter);

            if (!string.IsNullOrWhiteSpace(name))
                values.Add($"{name} = {value ?? "NULL"}");
        }

        return values;
    }

    private static IReadOnlyList<string> ExtractDbCommandParameters(DbCommand command)
    {
        var values = new List<string>();

        foreach (DbParameter parameter in command.Parameters)
            values.Add(FormatDbParameter(parameter));

        return values;
    }

    private static string FormatDbParameter(DbParameter parameter) =>
        $"{parameter.ParameterName} = {parameter.Value ?? "NULL"}";

    private static IReadOnlyList<string> ExtractParameters(string normalized)
    {
        var parameters = new List<string>();

        foreach (var line in normalized.Split('\n', StringSplitOptions.TrimEntries))
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
        DbLogSettings settings,
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

            var logLevelValue = binding.LogLevelEnumType is null
                ? (object)(int)settings.Level
                : Enum.ToObject(binding.LogLevelEnumType, (int)settings.Level);

            if (parameters.Length == 2)
            {
                subscription = binding.Method.Invoke(null, new object?[] { databaseFacade, logAction }) as IDisposable;

                return subscription is not null;
            }

            if (parameters.Length == 3 || settings.Verbose)
            {
                subscription = binding.Method.Invoke(
                    null,
                    new object?[] { databaseFacade, logAction, logLevelValue }) as IDisposable;

                return subscription is not null;
            }

            subscription = binding.Method.Invoke(
                null,
                new object?[]
                {
                    databaseFacade,
                    logAction,
                    logLevelValue,
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

    private sealed class DiagnosticEventObserver : IObserver<KeyValuePair<string, object?>>
    {
        private readonly EfSqlCapture _capture;
        private readonly DiagnosticSqlCaptureBinding _binding;
        private readonly bool _verbose;

        internal DiagnosticEventObserver(EfSqlCapture capture, DiagnosticSqlCaptureBinding binding, bool verbose)
        {
            _capture = capture;
            _binding = binding;
            _verbose = verbose;
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is null)
                return;

            if (_verbose)
            {
                if (value.Value.GetType().GetProperty("Context") is null)
                    return;

                _capture.OnVerboseDiagnosticEvent(value.Key, value.Value);
                return;
            }

            if (!string.Equals(value.Key, _binding.CommandExecutedEventName, StringComparison.Ordinal))
                return;

            if (!_binding.CommandExecutedEventDataType.IsInstanceOfType(value.Value))
                return;

            _capture.OnCommandExecutedDiagnosticEvent(value.Value);
        }
    }

    private sealed class DiagnosticListenerObserver : IObserver<DiagnosticListener>
    {
        private readonly EfSqlCapture _capture;
        private readonly DiagnosticSqlCaptureBinding _binding;
        private readonly bool _verbose;

        internal DiagnosticListenerObserver(EfSqlCapture capture, DiagnosticSqlCaptureBinding binding, bool verbose)
        {
            _capture = capture;
            _binding = binding;
            _verbose = verbose;
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(DiagnosticListener listener)
        {
            if (!string.Equals(listener.Name, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
                return;

            _capture._listenerSubscriptions.Add(
                listener.Subscribe(new DiagnosticEventObserver(_capture, _binding, _verbose)));
        }
    }
}
