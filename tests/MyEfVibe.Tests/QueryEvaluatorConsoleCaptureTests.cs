using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class QueryEvaluatorConsoleCaptureTests
{
    [Fact]
    public async Task EvaluateAsync_when_console_capture_is_disabled_writes_to_current_console()
    {
        await using var context = CreateContext();
        using var assemblyLoader = CreateAssemblyLoader();
        var workspaceAssemblyPaths = CreateWorkspaceAssemblyPaths();
        var session = new ScriptSession(
            typeof(ProbeHistoryDbContext),
            context,
            workspaceAssemblyPaths,
            assemblyLoader);

        var previous = Console.Out;
        using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);

            var (_, metrics) = await QueryEvaluator.EvaluateAsync(
                context,
                session,
                "Console.WriteLine(\"hello\");",
                new DbLogSettings { Enabled = false },
                [typeof(ProbeHistoryDbContext).Assembly],
                captureConsoleOutput: false);

            Assert.Equal($"hello{Environment.NewLine}", writer.ToString());
            Assert.Null(metrics.ConsoleOutput);
        }
        finally
        {
            Console.SetOut(previous);
        }
    }

    [Fact]
    public async Task EvaluateAsync_when_console_capture_is_enabled_keeps_stdout_protocol_clean()
    {
        await using var context = CreateContext();
        using var assemblyLoader = CreateAssemblyLoader();
        var workspaceAssemblyPaths = CreateWorkspaceAssemblyPaths();
        var session = new ScriptSession(
            typeof(ProbeHistoryDbContext),
            context,
            workspaceAssemblyPaths,
            assemblyLoader);

        var previous = Console.Out;
        using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);

            var (_, metrics) = await QueryEvaluator.EvaluateAsync(
                context,
                session,
                "Console.WriteLine(\"hello\");",
                new DbLogSettings { Enabled = false },
                [typeof(ProbeHistoryDbContext).Assembly],
                captureConsoleOutput: true);

            Assert.Equal(string.Empty, writer.ToString());
            Assert.Equal("hello", metrics.ConsoleOutput);
        }
        finally
        {
            Console.SetOut(previous);
        }
    }

    private static ProbeHistoryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ProbeHistoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ProbeHistoryDbContext(options);
    }

    private static InteractiveAssemblyLoader CreateAssemblyLoader()
    {
        var assemblyLoader = new InteractiveAssemblyLoader();

        assemblyLoader.RegisterDependency(typeof(ProbeHistoryDbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(DbContext).Assembly);

        return assemblyLoader;
    }

    private static ImmutableHashSet<string> CreateWorkspaceAssemblyPaths()
    {
        return new[]
            {
                typeof(ProbeHistoryDbContext).Assembly.Location,
                typeof(DbContext).Assembly.Location
            }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
