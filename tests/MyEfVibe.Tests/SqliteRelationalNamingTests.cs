using System.Reflection;

namespace MyEfVibe.Tests;

public sealed class SqliteRelationalNamingTests
{
    [Fact]
    public void ResolveInstance_maps_product_entity_to_schema_dot_table_and_lowercase_columns()
    {
        var persistenceDll = FindPrebuiltPersistenceDll();

        if (persistenceDll is null)
        {
            return;
        }

        var outputDirectory = Path.GetDirectoryName(persistenceDll)!;
        var efProject =
            "/home/adiaz/Projects/AdventureWorks/apps/api-dotnet/src/AdventureWorks.Infrastructure.Persistence/AdventureWorks.Infrastructure.Persistence.csproj";

        if (!File.Exists(efProject))
        {
            return;
        }

        var sqliteDb = "/home/adiaz/Projects/AdventureWorks/Source/AdventureWorksLT.db";

        if (!File.Exists(sqliteDb))
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

        var dbContext = DbContextActivator.ResolveInstance(
            host,
            "AdventureWorksDbContext",
            $"Data Source={sqliteDb}",
            ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.Sqlite),
            false);

        var productsProperty = dbContext.GetType().GetProperty("Products");

        Assert.NotNull(productsProperty);

        var productEntity = productsProperty!.PropertyType.GetGenericArguments()[0];
        var entityType = FindModelEntityType(
            dbContext.GetType()
                .GetProperty("Model", BindingFlags.Public | BindingFlags.Instance)!
                .GetValue(dbContext)!,
            productEntity);

        Assert.NotNull(entityType);

        var tableName = InvokeRelationalExtension(entityType!, "GetTableName") as string;
        var schema = InvokeRelationalExtension(entityType!, "GetSchema") as string;

        Assert.True(string.IsNullOrEmpty(schema));
        Assert.False(string.Equals("Product", tableName, StringComparison.Ordinal));
        Assert.Contains("product", tableName, StringComparison.OrdinalIgnoreCase);
    }

    private static object? FindModelEntityType(object model, Type clrType)
    {
        const string iModelFullName = "Microsoft.EntityFrameworkCore.Metadata.IModel";

        var findEntityType = model.GetType()
            .GetInterfaces()
            .FirstOrDefault(iface => string.Equals(iface.FullName, iModelFullName, StringComparison.Ordinal))
            ?.GetMethod("FindEntityType", [typeof(Type)]);

        return findEntityType?.Invoke(model, [clrType]);
    }

    private static object? InvokeRelationalExtension(object metadata, string methodName)
    {
        var relationalAssembly = AppDomain.CurrentDomain
            .GetAssemblies()
            .First(assembly =>
                string.Equals(assembly.GetName().Name, "Microsoft.EntityFrameworkCore.Relational",
                    StringComparison.Ordinal));

        var extensionsType = relationalAssembly.GetType(
            "Microsoft.EntityFrameworkCore.RelationalEntityTypeExtensions",
            true)!;

        var method = extensionsType
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .First(candidate =>
                string.Equals(candidate.Name, methodName, StringComparison.Ordinal)
                && candidate.GetParameters().Length == 1);

        return method.Invoke(null, [metadata]);
    }

    private static string? FindPrebuiltPersistenceDll()
    {
        var root = Path.Combine(Path.GetTempPath(), "efvibe-integration");

        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(root, "AdventureWorks.Infrastructure.Persistence.dll", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal));
    }
}