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
    private readonly ScriptOptions _options;
    private readonly object _globals;
    private readonly Type _globalsType;
    private readonly Type _dbContextType;
    private readonly ImmutableArray<string> _importNamespaces;
    private readonly InteractiveAssemblyLoader _assemblyLoader;
    private readonly List<string> _submissionHistory = new();
    private Script? _script;
    private ScriptState? _state;

    internal ImmutableArray<MetadataReference> MetadataReferences { get; }

    internal CSharpCompilationOptions CompilationOptions { get; } =
        new(OutputKind.DynamicallyLinkedLibrary);

    internal ScriptSession(
        Type dbContextType,
        object dbContextInstance,
        ImmutableHashSet<string> workspaceAssemblyPaths,
        InteractiveAssemblyLoader assemblyLoader)
    {
        _assemblyLoader = assemblyLoader;
        _dbContextType = dbContextType;
        _globalsType = typeof(ScriptGlobals<>).MakeGenericType(dbContextType);
        _globals = Activator.CreateInstance(_globalsType)!;
        _globalsType.GetProperty("db")!.SetValue(_globals, dbContextInstance);

        var referencePaths = MetadataPathComposer.Compose(workspaceAssemblyPaths);

        var metadataReferences = ImmutableArray.CreateBuilder<MetadataReference>(referencePaths.Length);

        foreach (var path in referencePaths)
            metadataReferences.Add(MetadataReference.CreateFromFile(path));

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
            "Microsoft.EntityFrameworkCore.Infrastructure",
        };

        importNamespaces.AddRange(CollectWorkspaceNamespaces(workspaceAssemblyPaths));
        _importNamespaces = importNamespaces.ToImmutableArray();

        _options = ScriptOptions.Default
            .AddReferences(MetadataReferences)
            .WithImports(_importNamespaces);
    }

    internal (string source, int position, int currentLineStart) CreateCompletionSource(
        string currentLine,
        int cursorInLine)
    {
        const string submissionIndent = "        ";

        var builder = new StringBuilder();

        foreach (var importNamespace in _importNamespaces)
            builder.AppendLine($"using {importNamespace};");

        builder.AppendLine();
        builder.AppendLine("internal static class __MyEfVibeCompletionHost");
        builder.AppendLine("{");
        builder.AppendLine("    internal static void __Complete()");
        builder.AppendLine("    {");

        var dbContextTypeName = _dbContextType.FullName!;

        builder.Append(submissionIndent);
        builder.AppendLine($"{dbContextTypeName} db = null!;");

        foreach (var submission in _submissionHistory)
        {
            foreach (var submissionLine in submission.Split('\n'))
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

    internal async Task<object?> EvaluateAsync(string code, CancellationToken cancellationToken = default)
    {
        var trimmed = SnippetNormalizer.ForEvaluation(code);

        if (string.IsNullOrEmpty(trimmed))
            return null;

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
            throw _state.Exception is Exception concrete
                ? concrete
                : new Exception(_state.Exception.ToString());

        RecordSubmission(trimmed);

        return _state.ReturnValue;
    }

    internal void RecordSubmission(string snippet)
    {
        var normalized = snippet.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
            return;

        _submissionHistory.Add(normalized);
    }

    internal void Reset()
    {
        _state = null;
        _script = null;
        _submissionHistory.Clear();
    }

    private static IEnumerable<string> CollectWorkspaceNamespaces(ImmutableHashSet<string> workspaceAssemblyPaths)
    {
        var namespaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assemblyPath in workspaceAssemblyPaths)
        {
            if (!File.Exists(assemblyPath))
                continue;

            if (assemblyPath.Contains("Microsoft.", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var assembly = System.Reflection.Assembly.LoadFrom(assemblyPath);

                foreach (var exported in ReflectionToolkit.EnumerateLoadableExportedTypes(assembly))
                {
                    if (!string.IsNullOrWhiteSpace(exported.Namespace))
                        namespaces.Add(exported.Namespace);
                }
            }
            catch
            {
            }
        }

        return namespaces;
    }
}
