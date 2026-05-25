using System.Reflection;

namespace MyEfVibe;

/// <summary>
/// Applies optional provider builder extensions (e.g. <c>UseNetTopologySuite</c>) when the EF project
/// references the corresponding satellite packages — mirrors <c>UseSqlServer(conn, o =&gt; o.UseNetTopologySuite())</c>.
/// </summary>
internal static class ProviderOptionsConfigurator
{
    private readonly record struct OptionalProviderExtension(string AssemblyName, string ExtensionMethodName);

    private static readonly OptionalProviderExtension[] SqlServerExtensions =
    [
        new("Microsoft.EntityFrameworkCore.SqlServer.NetTopologySuite", "UseNetTopologySuite"),
        new("Microsoft.EntityFrameworkCore.SqlServer.HierarchyId", "UseHierarchyId"),
    ];

    private static readonly OptionalProviderExtension[] NpgsqlExtensions =
    [
        new("Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite", "UseNetTopologySuite"),
    ];

    private static readonly OptionalProviderExtension[] MySqlExtensions =
    [
        new("Pomelo.EntityFrameworkCore.MySql.NetTopologySuite", "UseNetTopologySuite"),
    ];

    internal static bool HasOptionalExtensions(WorkspaceHost host, MyEfVibeProvider provider) =>
        GetExtensions(provider).Any(extension => host.LoadAssembly(extension.AssemblyName) is not null);

    internal static void Apply(WorkspaceHost host, MyEfVibeProvider provider, object providerOptionsBuilder)
    {
        foreach (var extension in GetExtensions(provider))
        {
            if (host.LoadAssembly(extension.AssemblyName) is null)
                continue;

            TryInvokeProviderBuilderExtension(
                host,
                extension.AssemblyName,
                extension.ExtensionMethodName,
                providerOptionsBuilder);
        }
    }

    /// <summary>
    /// Chains <c>UseSnakeCaseNamingConvention</c> (or lowercase) on <see cref="DbContextOptionsBuilder"/>
    /// when the EF project references <c>EFCore.NamingConventions</c> — required for PostgreSQL samples that
    /// map <c>Production.Product</c> to <c>production.product</c>.
    /// </summary>
    internal static void TryApplyEfCoreNamingConventions(
        WorkspaceHost host,
        object dbContextOptionsBuilder,
        MyEfVibeProvider providerKey)
    {
        if (providerKey != MyEfVibeProvider.Npgsql)
            return;

        TryRegisterNpgsqlModelCustomizer(host, dbContextOptionsBuilder);

        foreach (var methodName in new[] { "UseSnakeCaseNamingConvention", "UseLowerCaseNamingConvention" })
        {
            if (TryInvokeDbContextOptionsBuilderExtension(
                    host,
                    "EFCore.NamingConventions",
                    methodName,
                    dbContextOptionsBuilder))
                return;
        }
    }

    private static void TryRegisterNpgsqlModelCustomizer(WorkspaceHost host, object dbContextOptionsBuilder)
    {
        host.PreloadPackageByName("Microsoft.EntityFrameworkCore");

        var efAssembly = host.LoadAssembly("Microsoft.EntityFrameworkCore");

        if (efAssembly is null)
            return;

        var modelCustomizerType = efAssembly.GetType(
            "Microsoft.EntityFrameworkCore.Infrastructure.IModelCustomizer",
            throwOnError: false);

        var replacementType = NpgsqlModelCustomizerEmitter.TryGetOrCreate(host);

        if (modelCustomizerType is null || replacementType is null)
            return;

        foreach (var replaceMethod in dbContextOptionsBuilder.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!string.Equals(replaceMethod.Name, "ReplaceService", StringComparison.Ordinal)
                || !replaceMethod.IsGenericMethodDefinition)
                continue;

            var parameters = replaceMethod.GetParameters();

            if (parameters.Length != 0)
                continue;

            try
            {
                var closed = replaceMethod.MakeGenericMethod(modelCustomizerType, replacementType);
                _ = closed.Invoke(dbContextOptionsBuilder, null);
                return;
            }
            catch
            {
                // Fall back to AdventureWorks OnModelCreating when ReplaceService is unavailable.
            }
        }
    }

    internal static bool TryInvokeUseProviderWithOptions(
        WorkspaceHost host,
        Assembly providerAssembly,
        object closedBuilderInstance,
        string connectionString,
        string useProviderMethodName,
        MyEfVibeProvider providerKey)
    {
        if (!HasOptionalExtensions(host, providerKey))
            return false;

        foreach (var exported in ReflectionToolkit.EnumerateLoadableExportedTypes(providerAssembly))
        foreach (var staticMethodCandidate in exported.GetMethods(BindingFlags.Static | BindingFlags.Public
                                                                   | BindingFlags.NonPublic))
        {
            if (!staticMethodCandidate.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false))
                continue;

            if (!string.Equals(staticMethodCandidate.Name, useProviderMethodName, StringComparison.Ordinal))
                continue;

            var parametersDetailed = staticMethodCandidate.GetParameters();

            if (parametersDetailed.Length != 3)
                continue;

            if (!parametersDetailed[0].ParameterType.IsAssignableFrom(closedBuilderInstance.GetType()))
                continue;

            if (parametersDetailed[1].ParameterType != typeof(string))
                continue;

            if (!parametersDetailed[2].ParameterType.IsGenericType
                || parametersDetailed[2].ParameterType.GetGenericTypeDefinition() != typeof(Action<>))
                continue;

            var configurator = new ProviderOptionsDelegate(host, providerKey);
            var configureMethod = typeof(ProviderOptionsDelegate).GetMethod(
                nameof(ProviderOptionsDelegate.Configure),
                BindingFlags.Instance | BindingFlags.Public)!;
            var configureDelegate = Delegate.CreateDelegate(parametersDetailed[2].ParameterType, configurator, configureMethod);

            staticMethodCandidate.Invoke(null, [closedBuilderInstance, connectionString, configureDelegate]);

            return true;
        }

        return false;
    }

    private static IEnumerable<OptionalProviderExtension> GetExtensions(MyEfVibeProvider provider) =>
        provider switch
        {
            MyEfVibeProvider.SqlServer => SqlServerExtensions,
            MyEfVibeProvider.Npgsql => NpgsqlExtensions,
            MyEfVibeProvider.MySql or MyEfVibeProvider.MariaDb => MySqlExtensions,
            _ => Array.Empty<OptionalProviderExtension>(),
        };

    private static bool TryInvokeDbContextOptionsBuilderExtension(
        WorkspaceHost host,
        string extensionAssemblyName,
        string extensionMethodName,
        object dbContextOptionsBuilder) =>
        TryInvokeProviderBuilderExtension(host, extensionAssemblyName, extensionMethodName, dbContextOptionsBuilder);

    private static bool TryInvokeProviderBuilderExtension(
        WorkspaceHost host,
        string extensionAssemblyName,
        string extensionMethodName,
        object providerOptionsBuilder)
    {
        var extensionAssembly = host.LoadAssembly(extensionAssemblyName);

        if (extensionAssembly is null)
            return false;

        var builderType = providerOptionsBuilder.GetType();

        foreach (var exported in ReflectionToolkit.EnumerateLoadableExportedTypes(extensionAssembly))
        foreach (var staticMethodCandidate in exported.GetMethods(BindingFlags.Static | BindingFlags.Public
                                                                   | BindingFlags.NonPublic))
        {
            if (!staticMethodCandidate.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false))
                continue;

            if (!string.Equals(staticMethodCandidate.Name, extensionMethodName, StringComparison.Ordinal))
                continue;

            var parametersDetailed = staticMethodCandidate.GetParameters();

            if (parametersDetailed.Length != 1
                || !parametersDetailed[0].ParameterType.IsAssignableFrom(builderType))
                continue;

            staticMethodCandidate.Invoke(null, [providerOptionsBuilder]);

            return true;
        }

        return false;
    }

    private sealed class ProviderOptionsDelegate(WorkspaceHost host, MyEfVibeProvider provider)
    {
        public void Configure(object providerOptionsBuilder) =>
            ProviderOptionsConfigurator.Apply(host, provider, providerOptionsBuilder);
    }
}
