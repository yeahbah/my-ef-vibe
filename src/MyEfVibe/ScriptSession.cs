using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;

namespace MyEfVibe;

internal sealed class ScriptSession
{
    private readonly InteractiveAssemblyLoader _assemblyLoader;
    private readonly object _globals;
    private readonly Type _globalsType;
    private readonly ImmutableArray<string> _searchPaths;
    private readonly ScriptSessionConfiguration _configuration;
    private readonly ImmutableArray<string> _importNamespaces;
    private readonly ScriptOptions _options;
    private readonly string _scriptBasePath;
    private readonly List<string> _submissionHistory = [];
    private bool _bootstrapCompleted;
    private Script? _script;
    private ScriptState? _state;

    internal ScriptSession(
        Type dbContextType,
        object dbContextInstance,
        ImmutableHashSet<string> workspaceAssemblyPaths,
        InteractiveAssemblyLoader assemblyLoader,
        bool preserveAsyncQueries = false,
        ScriptSessionConfiguration? configuration = null,
        string? scriptSearchBasePath = null)
    {
        _configuration = configuration ?? ScriptSessionConfiguration.Empty;
        _assemblyLoader = assemblyLoader;
        DbContextType = dbContextType;
        PreserveAsyncQueries = preserveAsyncQueries;
        _globalsType = typeof(ScriptGlobals<>).MakeGenericType(dbContextType);
        _globals = Activator.CreateInstance(_globalsType)!;
        _globalsType.GetProperty("db")!.SetValue(_globals, dbContextInstance);

        var referencePaths = MetadataPathComposer.Compose(workspaceAssemblyPaths);

        var metadataReferences = ImmutableArray.CreateBuilder<MetadataReference>(referencePaths.Length);

        foreach (var path in referencePaths)
        {
            metadataReferences.Add(MetadataReference.CreateFromFile(path));
        }

        MetadataReferences = metadataReferences.ToImmutable();

        var importNamespaces = new List<string>
        {
            "System",
            "System.Collections",
            "System.Collections.Generic",
            "System.Globalization",
            "System.IO",
            "System.Linq",
            "System.Linq.Expressions",
            "System.Text",
            "System.Threading",
            "System.Threading.Tasks",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Infrastructure"
        };

        importNamespaces.AddRange(_configuration.AdditionalUsings);
        importNamespaces.AddRange(
            ScriptNamespaceImports.FilterWorkspaceNamespaces(CollectWorkspaceNamespaces(workspaceAssemblyPaths)));

        _importNamespaces = [
            ..importNamespaces
                .Distinct(StringComparer.Ordinal)
        ];

        var fallbackSearchBasePath = string.IsNullOrWhiteSpace(scriptSearchBasePath)
            ? Directory.GetCurrentDirectory()
            : scriptSearchBasePath;
        var searchPaths = _configuration.ResolveSearchPaths(fallbackSearchBasePath);
        _scriptBasePath = _configuration.ResolveBasePath(fallbackSearchBasePath);
        _searchPaths = searchPaths;

        _options = ScriptOptions.Default
            .AddReferences(MetadataReferences)
            .WithImports(_importNamespaces)
            .WithFilePath(Path.Combine(_scriptBasePath, "__efvibe_query__.csx"))
            .WithSourceResolver(new SourceFileResolver(searchPaths, _scriptBasePath));
    }

    internal ImmutableArray<MetadataReference> MetadataReferences { get; }

    internal Type DbContextType { get; }

    internal bool PreserveAsyncQueries { get; }

    internal object DbContext => _globalsType.GetProperty("db")!.GetValue(_globals)!;

    internal CSharpCompilationOptions CompilationOptions { get; } =
        new(OutputKind.DynamicallyLinkedLibrary);

    internal (string source, int position, int currentLineStart) CreateCompletionSource(
        string currentLine,
        int cursorInLine)
    {
        const string submissionIndent = "        ";

        var builder = new StringBuilder();

        foreach (var importNamespace in _importNamespaces)
        {
            builder.AppendLine($"using {importNamespace};");
        }

        builder.AppendLine();
        builder.AppendLine("internal static class __MyEfVibeCompletionHost");
        builder.AppendLine("{");
        builder.AppendLine("    internal static void __Complete()");
        builder.AppendLine("    {");

        var dbContextTypeName = DbContextType.FullName!;

        builder.Append(submissionIndent);
        builder.AppendLine($"{dbContextTypeName} db = null!;");

        foreach (var submission in _submissionHistory)
        {
            foreach (var submissionLine in InputLineUtilities.SplitLines(submission))
            {
                builder.Append(submissionIndent);
                builder.AppendLine(submissionLine);
            }

            if (!submission.TrimEnd().EndsWith(';'))
            {
                builder.Append(submissionIndent);
                builder.AppendLine(";");
            }
        }

        var safeCursor = Math.Clamp(cursorInLine, 0, currentLine.Length);

        var currentLineStart = builder.Length;

        builder.Append(submissionIndent);
        builder.Append(currentLine.AsSpan(0, safeCursor));

        var position = builder.Length;

        builder.AppendLine();
        builder.AppendLine("    }");
        builder.AppendLine("}");

        return (builder.ToString(), position, currentLineStart);
    }

    internal async Task InitializeAsync(
        string scriptSearchBasePath,
        CancellationToken cancellationToken = default)
    {
        if (_bootstrapCompleted)
        {
            return;
        }

        var loadPaths = _configuration.ResolveLoadPaths(scriptSearchBasePath);

        foreach (var loadPath in loadPaths)
        {
            var loadedCode = await File.ReadAllTextAsync(loadPath, cancellationToken);
            await EvaluateScriptFragmentAsync(loadedCode, cancellationToken);
            RecordSubmission(ScriptLoadDirectiveResolver.FormatLoadDirective(loadPath));
        }

        _bootstrapCompleted = true;
    }

    internal async Task<object?> EvaluateAsync(string code, CancellationToken cancellationToken = default)
    {
        await EnsureBootstrapAsync(cancellationToken);

        var trimmed = SnippetNormalizer.ForEvaluation(code, DbContextType, PreserveAsyncQueries);
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var (directives, body) = ScriptDirectiveSplitter.SplitLeadingDirectives(trimmed);

        if (!string.IsNullOrWhiteSpace(directives))
        {
            await EvaluateBootstrapAsync(directives, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        trimmed = ScriptAttributeParser.StripScriptAttributeLines(body);

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            if (ScriptAttributeParser.ContainsScriptAttributeLines(body))
            {
                throw new CompilationEvaluationException(
                    "Script attribute lines such as #[Benchmark(N)] must run with the full script. Use Run All.");
            }

            return null;
        }

        await EvaluateScriptFragmentAsync(trimmed, cancellationToken);
        RecordSubmission(trimmed);

        return await UnwrapTaskReturnValueAsync(_state!.ReturnValue, cancellationToken);
    }

    /// <summary>
    ///     Evaluates a one-off expression without advancing REPL submission state or history.
    ///     Used for SQL translation probes before the user's query runs.
    /// </summary>
    internal async Task<object?> EvaluateProbeAsync(string code, CancellationToken cancellationToken = default)
    {
        await EnsureBootstrapAsync(cancellationToken);

        var trimmed = SnippetNormalizer.ForEvaluation(
            ScriptAttributeParser.StripScriptAttributeLines(
                ProbeScriptFormatter.ToScriptExpression(code)),
            DbContextType,
            PreserveAsyncQueries);

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var script = CSharpScript.Create(trimmed, _options, _globalsType, _assemblyLoader);

        ScriptState state;

        try
        {
            state = await script.RunAsync(_globals, cancellationToken);
        }
        catch (CompilationErrorException compilationFailure)
        {
            var messages = compilationFailure.Diagnostics
                .Where(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Hidden)
                .Select(static diagnostic => diagnostic.ToString())
                .ToArray();

            throw messages.Length == 0
                ? new CompilationEvaluationException(compilationFailure.Message)
                : new CompilationEvaluationException(string.Join(Environment.NewLine, messages));
        }

        if (state.Exception is not null)
        {
            throw state.Exception is Exception concrete
                ? concrete
                : new Exception(state.Exception.ToString());
        }

        return await UnwrapTaskReturnValueAsync(state.ReturnValue, cancellationToken);
    }

    private static async Task<object?> UnwrapTaskReturnValueAsync(
        object? returnValue,
        CancellationToken cancellationToken)
    {
        if (returnValue is not Task task)
        {
            return returnValue;
        }

        await task.WaitAsync(cancellationToken).ConfigureAwait(false);

        var taskType = task.GetType();

        if (!taskType.IsGenericType)
        {
            return null;
        }

        return taskType.GetProperty("Result")?.GetValue(task);
    }

    internal void RecordSubmission(string snippet)
    {
        var normalized = snippet.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        _submissionHistory.Add(normalized);
    }

    internal void Reset()
    {
        _state = null;
        _script = null;
        _submissionHistory.Clear();
        _bootstrapCompleted = false;
    }

    private async Task EnsureBootstrapAsync(CancellationToken cancellationToken)
    {
        if (_bootstrapCompleted)
        {
            return;
        }

        await InitializeAsync(_scriptBasePath, cancellationToken);
    }

    private async Task EvaluateBootstrapAsync(string code, CancellationToken cancellationToken)
    {
        foreach (var line in InputLineUtilities.SplitLines(code))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (ScriptLoadDirectiveResolver.TryParseLoadPath(line) is { } loadPath)
            {
                var fullPath = ScriptLoadDirectiveResolver.ResolveLoadPath(
                    loadPath,
                    _searchPaths,
                    _scriptBasePath);
                var loadedCode = await File.ReadAllTextAsync(fullPath, cancellationToken);
                await EvaluateScriptFragmentAsync(loadedCode, cancellationToken);
                RecordSubmission(ScriptLoadDirectiveResolver.FormatLoadDirective(fullPath));
                continue;
            }

            await EvaluateScriptFragmentAsync(line, cancellationToken);
            RecordSubmission(line);
        }
    }

    private async Task EvaluateScriptFragmentAsync(string code, CancellationToken cancellationToken)
    {
        var trimmed = code.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        try
        {
            if (_state is null)
            {
                _script = CSharpScript.Create(
                    trimmed,
                    _options,
                    _globalsType,
                    _assemblyLoader);

                _state = await _script.RunAsync(_globals, cancellationToken);
            }
            else
            {
                _state = await _state.ContinueWithAsync(trimmed, cancellationToken: cancellationToken);
            }
        }
        catch (CompilationErrorException compilationFailure)
        {
            var messages = compilationFailure.Diagnostics
                .Where(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Hidden)
                .Select(static diagnostic => diagnostic.ToString())
                .ToArray();

            throw messages.Length == 0
                ? new CompilationEvaluationException(compilationFailure.Message)
                : new CompilationEvaluationException(string.Join(Environment.NewLine, messages));
        }

        if (_state.Exception is not null)
        {
            throw _state.Exception is Exception concrete
                ? concrete
                : new Exception(_state.Exception.ToString());
        }
    }

    private static IEnumerable<string> CollectWorkspaceNamespaces(ImmutableHashSet<string> workspaceAssemblyPaths)
    {
        var namespaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assemblyPath in workspaceAssemblyPaths)
        {
            if (!File.Exists(assemblyPath))
            {
                continue;
            }

            if (assemblyPath.Contains("Microsoft.", StringComparison.OrdinalIgnoreCase)
                || assemblyPath.Contains("System.Linq.Dynamic", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var assembly = Assembly.LoadFrom(assemblyPath);

                foreach (var exported in ReflectionToolkit.EnumerateLoadableExportedTypes(assembly))
                {
                    if (!string.IsNullOrWhiteSpace(exported.Namespace))
                    {
                        namespaces.Add(exported.Namespace);
                    }
                }
            }
            catch
            {
            }
        }

        return namespaces;
    }
}