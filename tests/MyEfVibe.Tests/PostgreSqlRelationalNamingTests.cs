using System.Reflection;

namespace MyEfVibe.Tests;

public sealed class PostgreSqlRelationalNamingTests
{
    [Fact]
    public void ResolveInstance_maps_product_entity_to_lowercase_postgresql_identifiers()
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
            "Host=localhost;Port=5432;Database=adventureworks;Username=postgres;Password=Your_strong_Password123!",
            MyEfVibeProvider.Npgsql,
            false);

        var customizerType = EfVibeModelCustomizerEmitter.TryGetOrCreate(
            host,
            typeof(PostgreSqlRelationalNamingApplier).GetMethod(
                nameof(PostgreSqlRelationalNamingApplier.CustomizeAfterBase),
                BindingFlags.Static | BindingFlags.Public)!);

        Assert.NotNull(customizerType);

        var productsProperty = dbContext.GetType().GetProperty("Products");

        Assert.NotNull(productsProperty);

        var productEntity = productsProperty!.PropertyType.GetGenericArguments()[0];

        var model = dbContext.GetType()
            .GetProperty("Model", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(dbContext)!;

        var entityType = FindModelEntityType(model, productEntity);

        Assert.NotNull(entityType);

        var getSchema = InvokeRelationalExtension(entityType!, "GetSchema") as string;
        var getTableName = InvokeRelationalExtension(entityType!, "GetTableName") as string;

        Assert.Equal("production", getSchema);
        Assert.Equal("product", getTableName);
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