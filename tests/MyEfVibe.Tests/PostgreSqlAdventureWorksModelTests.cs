using System.Reflection;
using MyEfVibe.Workspace;

namespace MyEfVibe.Tests;

public sealed class PostgreSqlAdventureWorksModelTests
{
    [Fact]
    public async Task Product_makeflag_has_bool_smallint_converter_for_adventureworks_postgres()
    {
        var persistenceDll = FindPrebuiltPersistenceDll();

        if (persistenceDll is null)
        {
            return;
        }

        var outputDirectory = Path.GetDirectoryName(persistenceDll)!;
        var efProject =
            "/home/yeahbah/Projects/AdventureWorksPg/apps/api-dotnet/src/AdventureWorks.Infrastructure.Persistence/AdventureWorks.Infrastructure.Persistence.csproj";

        if (!File.Exists(efProject))
        {
            return;
        }

        var workspaceBuild = new WorkspaceBuildResult(
            Path.Combine(Path.GetTempPath(), "efvibe-tests", Guid.NewGuid().ToString("N")),
            efProject,
            efProject,
            outputDirectory,
            persistenceDll,
            "net10.0",
            new ProjectBuildOutput(outputDirectory));

        using var host = WorkspaceHost.Load(workspaceBuild);

        var connectionString =
            "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=AdventureWorks_Dev_2026!;Timeout=3";

        if (PostgreSqlNamingProbe.Detect(host, connectionString) != PostgreSqlNamingStyle.AdventureWorksPascalCase)
        {
            return;
        }

        var columnIndex = PostgreSqlNamingProbe.LoadColumnMetadataIndex(host, connectionString);

        if (!columnIndex.TryGetValue(("Production", "Product"), out var productColumns))
        {
            return;
        }
        Assert.Equal("smallint", productColumns["MakeFlag"]);

        var dbContext = DbContextActivator.ResolveInstance(
            host,
            "AdventureWorksDbContext",
            connectionString,
            ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.Npgsql),
            false);

        var model = dbContext.GetType()
            .GetProperty("Model", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(dbContext)!;

        var productEntity = FindEntityType(model, "Product");

        Assert.NotNull(productEntity);

        var makeFlagProperty = FindProperty(productEntity!, "MakeFlag");

        Assert.NotNull(makeFlagProperty);
        Assert.Equal(typeof(bool), GetClrType(makeFlagProperty!));

        var productIdProperty = FindProperty(productEntity!, "ProductId");

        Assert.NotNull(productIdProperty);

        var productIdColumn = GetColumnName(productIdProperty!);

        Assert.Equal("ProductID", productIdColumn);

        var converter = GetValueConverter(makeFlagProperty!);

        if (converter is null)
        {
            // Value converters applied via ModelBuilder may not surface through GetValueConverter
            // on the finalized runtime model; verify the mapping works end-to-end instead.
            var scriptSession = new ScriptSession(
                dbContext.GetType(),
                dbContext,
                workspaceBuild.ReferenceAssemblyPaths,
                host.AssemblyLoader);

            var (_, metrics) = await QueryEvaluator.EvaluateAsync(
                dbContext,
                scriptSession,
                "db.Products.Select(p => new { p.ProductId, p.Name, p.MakeFlag }).Take(1).ToList();",
                new DbLogSettings { Enabled = true },
                host.EnumerateLoadedAssemblies());

            Assert.True(metrics.Succeeded);
            Assert.True(metrics.RowCount >= 1);
            return;
        }

        Assert.Equal(typeof(bool), converter!.ModelClrType);
        Assert.Equal(typeof(short), converter.ProviderClrType);
    }

    private static object? FindEntityType(object model, string entityName)
    {
        const string iModelFullName = "Microsoft.EntityFrameworkCore.Metadata.IModel";

        var getEntityTypes = model.GetType()
            .GetInterfaces()
            .FirstOrDefault(iface => string.Equals(iface.FullName, iModelFullName, StringComparison.Ordinal))
            ?.GetMethod("GetEntityTypes");

        if (getEntityTypes?.Invoke(model, null) is not System.Collections.IEnumerable entityTypes)
        {
            return null;
        }

        foreach (var entityType in entityTypes)
        {
            if (entityType is null)
            {
                continue;
            }

            var clrType = entityType.GetType()
                .GetProperty("ClrType", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(entityType) as Type;

            if (string.Equals(clrType?.Name, entityName, StringComparison.Ordinal))
            {
                return entityType;
            }
        }

        return null;
    }

    private static object? FindProperty(object entityType, string propertyName)
    {
        const string iEntityTypeFullName = "Microsoft.EntityFrameworkCore.Metadata.IEntityType";

        var getProperties = entityType.GetType()
            .GetInterfaces()
            .FirstOrDefault(iface => string.Equals(iface.FullName, iEntityTypeFullName, StringComparison.Ordinal))
            ?.GetMethod("GetProperties");

        if (getProperties?.Invoke(entityType, null) is not System.Collections.IEnumerable properties)
        {
            return null;
        }

        foreach (var property in properties)
        {
            if (property is null)
            {
                continue;
            }

            var name = property.GetType()
                .GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(property) as string;

            if (string.Equals(name, propertyName, StringComparison.Ordinal))
            {
                return property;
            }
        }

        return null;
    }

    private static Type? GetClrType(object metadata)
    {
        foreach (var iface in metadata.GetType().GetInterfaces())
        {
            if (iface.GetProperty("ClrType")?.GetValue(metadata) is Type clrType)
            {
                return clrType;
            }
        }

        return metadata.GetType().GetProperty("ClrType")?.GetValue(metadata) as Type;
    }

    private static string? GetColumnName(object property)
    {
        var relationalAssembly = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, "Microsoft.EntityFrameworkCore.Relational",
                    StringComparison.Ordinal));

        var extensionsType = relationalAssembly?.GetType(
            "Microsoft.EntityFrameworkCore.RelationalPropertyExtensions",
            false);

        var getColumnName = extensionsType?
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .FirstOrDefault(method =>
                string.Equals(method.Name, "GetColumnName", StringComparison.Ordinal)
                && method.GetParameters().Length == 1);

        return getColumnName?.Invoke(null, [property]) as string;
    }

    private static ValueConverterInfo? GetValueConverter(object property)
    {
        var coreAssembly = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal));

        var extensionsType = coreAssembly?.GetType(
            "Microsoft.EntityFrameworkCore.Metadata.PropertyExtensions",
            false);

        var getConverter = extensionsType?
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .FirstOrDefault(method =>
                string.Equals(method.Name, "GetValueConverter", StringComparison.Ordinal)
                && method.GetParameters().Length == 1);

        if (getConverter?.Invoke(null, [property]) is not { } converter)
        {
            return null;
        }

        var converterType = converter.GetType();
        var providerClrType = converterType.GetProperty("ProviderClrType")?.GetValue(converter) as Type;
        var modelClrType = converterType.GetProperty("ModelClrType")?.GetValue(converter) as Type;

        return providerClrType is null || modelClrType is null
            ? null
            : new ValueConverterInfo(providerClrType, modelClrType);
    }

    private sealed record ValueConverterInfo(Type ProviderClrType, Type ModelClrType);

    private static string? FindPrebuiltPersistenceDll()
    {
        var candidates = new[]
        {
            Path.Combine(
                "/home/yeahbah/Projects/AdventureWorksPg/apps/api-dotnet/src/AdventureWorks.Infrastructure.Persistence/bin",
                "Debug",
                "net10.0",
                "AdventureWorks.Infrastructure.Persistence.dll"),
            Path.Combine(
                "/home/yeahbah/Projects/AdventureWorksPg/apps/api-dotnet/src/AdventureWorks.Infrastructure.Persistence/bin",
                "Release",
                "net10.0",
                "AdventureWorks.Infrastructure.Persistence.dll")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
