using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;

namespace MyEfVibe;

internal static class DbContextActivator
{
    internal static object ResolveInstance(WorkspaceHost host, string? contextFullName, string? connectionString,
        MyEfVibeProvider? provider)
    {
        var discoveredDbContextTypes =
            DiscoverDbContextConcreteTypes(host).OrderBy(static t => t.FullName, StringComparer.Ordinal).ToArray();

        if (discoveredDbContextTypes.Length == 0)
            throw new InvalidOperationException(
                "No concrete `Microsoft.EntityFrameworkCore.DbContext` implementations were discovered in the workspace output.");

        Type selectedDbContextType;

        if (!string.IsNullOrWhiteSpace(contextFullName))
        {
            selectedDbContextType =
                discoveredDbContextTypes.SingleOrDefault(t =>
                    string.Equals(t.FullName, contextFullName, StringComparison.Ordinal))

                ?? throw new InvalidOperationException(
                    $"DbContext `{contextFullName}` was not found. Known contexts:{Environment.NewLine}"
                    + string.Join(Environment.NewLine,
                        discoveredDbContextTypes.Select(static ctx => $" - {ctx.FullName}")));
        }
        else if (discoveredDbContextTypes.Length > 1)
        {
            throw new InvalidOperationException(
                "Multiple DbContext types were discovered. Choose one with `-c/--context`."
                + $"{Environment.NewLine}"
                + string.Join(Environment.NewLine,
                    discoveredDbContextTypes.Select(static ctx => $" - {ctx.FullName}")));
        }
        else
        {
            selectedDbContextType = discoveredDbContextTypes.Single();
        }

        if (TryCreateUsingDesignTimeFactory(selectedDbContextType, host, out var designTimeInstance))
            return designTimeInstance;

        if (TryCreateUsingParameterlessConstructor(selectedDbContextType, out var parameterlessInstance))
            return parameterlessInstance;

        if (string.IsNullOrWhiteSpace(connectionString)
            && AppSettingsConnectionResolver.TryResolve(host.OutputDirectory, out var fromSettings, out var inferredProvider))
        {
            connectionString = fromSettings;
            provider ??= inferredProvider;
        }

        if (!string.IsNullOrWhiteSpace(connectionString) && provider.HasValue
                                                         && TryCreateUsingOptionsConstructor(selectedDbContextType,
                                                             connectionString, provider.Value, host,
                                                             out var optionsInstance))
            return optionsInstance;

        throw new InvalidOperationException(
            "Unable to construct the DbContext automatically for this project."
            + $"{Environment.NewLine}"
            + "Typical fixes:"
            + $"{Environment.NewLine}"
            + " - Add an `IDesignTimeDbContextFactory<TContext>` implementation (recommended for tooling)."
            + $"{Environment.NewLine}"
            + " - Add a public parameterless constructor on the DbContext."
            + $"{Environment.NewLine}"
            + " - Pass `--connection-string` together with `--provider` (sqlserver | npgsql | sqlite) to build `DbContextOptions<TContext>`.");
    }

    private static ImmutableArray<Type> DiscoverDbContextConcreteTypes(WorkspaceHost host)
    {
        var distinctConcreteContexts = new HashSet<Type>();

        foreach (var assembly in host.EnumerateApplicationAssemblies())
        foreach (var exported in ReflectionToolkit.EnumerateLoadableExportedTypes(assembly))
        {
            if (!IsConcreteDbContext(exported))
                continue;

            distinctConcreteContexts.Add(exported);
        }

        return distinctConcreteContexts.ToImmutableArray();
    }

    private static bool IsConcreteDbContext(Type candidate)
    {
        if (!candidate.IsClass || candidate.IsAbstract)
            return false;

        for (var walk = candidate.BaseType; walk is not null; walk = walk.BaseType)
            if (string.Equals(walk.FullName, "Microsoft.EntityFrameworkCore.DbContext", StringComparison.Ordinal))
                return true;

        return false;
    }

    private static bool TryCreateUsingDesignTimeFactory(Type dbContextConcreteType, WorkspaceHost host,
        out object instance)
    {
        foreach (var assembly in host.EnumerateApplicationAssemblies())
        foreach (var exported in ReflectionToolkit.EnumerateLoadableExportedTypes(assembly))
        {
            if (exported.IsAbstract || !exported.IsClass)
                continue;

            if (!exported.GetInterfaces().Any(iface =>
                    IsDesignTimeFactoryInterface(iface, dbContextConcreteType)))
                continue;

            var factoryObject = Activator.CreateInstance(exported);

            if (factoryObject is null)
                continue;

            var createMethod =
                exported.GetMethods(BindingFlags.Instance | BindingFlags.Public).SingleOrDefault(methodDescriptor =>
                    string.Equals(methodDescriptor.Name, "CreateDbContext", StringComparison.Ordinal)


                    &&
                    methodDescriptor.GetParameters().Length == 0);

            if (createMethod is null || !dbContextConcreteType.IsAssignableFrom(createMethod.ReturnType))
                continue;

            var created = createMethod.Invoke(factoryObject, Array.Empty<object?>());

            if (created is null)
                continue;

            instance = created;

            return true;
        }

        instance = null!;

        return false;
    }

    private static bool IsDesignTimeFactoryInterface(Type iface, Type dbContextConcreteType)
        =>
            iface is { IsGenericType: true, Namespace: not null }


            && iface.GenericTypeArguments.Length == 1

            && iface.GenericTypeArguments[0] == dbContextConcreteType

            && iface.Namespace == "Microsoft.EntityFrameworkCore.Design"

            && iface.Name.StartsWith("IDesignTimeDbContextFactory", StringComparison.Ordinal);

    private static bool TryCreateUsingParameterlessConstructor(Type dbContextConcreteType, out object instance)
    {
        var parameterlessCtor = dbContextConcreteType.GetConstructor(Type.EmptyTypes);

        if (parameterlessCtor is null)
        {
            instance = null!;

            return false;
        }

        instance = Activator.CreateInstance(dbContextConcreteType)!;

        return true;
    }

    private static bool TryCreateUsingOptionsConstructor(Type dbContextConcreteType, string connectionString,
        MyEfVibeProvider providerKey, WorkspaceHost host, out object instance)
    {
        instance = null!;

        var efAssembly = LoadWorkspaceAssembly(host, "Microsoft.EntityFrameworkCore");

        if (efAssembly is null)
            return false;

        _ = LoadWorkspaceAssembly(host, "Microsoft.EntityFrameworkCore.Relational");

        var openBuilderType =
            efAssembly.GetType("Microsoft.EntityFrameworkCore.DbContextOptionsBuilder`1");

        if (openBuilderType is null)
            return false;

        var closedBuilderType =
            openBuilderType.MakeGenericType(dbContextConcreteType);

        var builderCtor = closedBuilderType.GetConstructor(Type.EmptyTypes);

        if (builderCtor is null)
            return false;

        var builderInstance = builderCtor.Invoke(Array.Empty<object?>());

        if (builderInstance is null)
            return false;

        if (!TryInvokeUseProviderExtension(host, builderInstance, connectionString, providerKey))
            return false;

        var closedOptionsType =
            efAssembly.GetType("Microsoft.EntityFrameworkCore.DbContextOptions`1")!.MakeGenericType(dbContextConcreteType);

        var optionsPropertyAccessor =
            closedBuilderType.GetProperty(
                "Options",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                returnType: closedOptionsType,
                types: Type.EmptyTypes,
                modifiers: null);

        var compiledOptionsConcreteInstance = optionsPropertyAccessor?.GetValue(builderInstance);

        if (compiledOptionsConcreteInstance is null)
            return false;

        var matchingCtor = dbContextConcreteType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(ctorCandidate =>
                ctorCandidate.GetParameters() is [{ ParameterType: var optionsParameter }]
                && optionsParameter.IsAssignableFrom(compiledOptionsConcreteInstance.GetType()));

        if (matchingCtor is null)
            return false;

        var createdContextInstance = matchingCtor.Invoke(new[] { compiledOptionsConcreteInstance });

        if (createdContextInstance is null)
            return false;

        instance = createdContextInstance;

        return true;
    }

    private static Assembly? LoadWorkspaceAssembly(WorkspaceHost host, string assemblyName)
    {
        var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly =>
            string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));

        if (alreadyLoaded is not null)
            return alreadyLoaded;

        var candidatePath = Path.Combine(host.OutputDirectory, $"{assemblyName}.dll");

        if (!File.Exists(candidatePath))
            return null;

        return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidatePath);
    }

    private static readonly IReadOnlyDictionary<MyEfVibeProvider, string> ProviderExtensionAssemblyNames =
        new Dictionary<MyEfVibeProvider, string>
        {
            [MyEfVibeProvider.SqlServer] = "Microsoft.EntityFrameworkCore.SqlServer",
            [MyEfVibeProvider.Npgsql] = "Npgsql.EntityFrameworkCore.PostgreSQL",
            [MyEfVibeProvider.Sqlite] = "Microsoft.EntityFrameworkCore.Sqlite",
        };

    private static bool TryInvokeUseProviderExtension(WorkspaceHost host, object closedBuilderInstance,
        string connectionString, MyEfVibeProvider providerKey)
    {
        var methodName = providerKey switch
        {
            MyEfVibeProvider.SqlServer => "UseSqlServer",
            MyEfVibeProvider.Npgsql => "UseNpgsql",
            MyEfVibeProvider.Sqlite => "UseSqlite",
            _ => string.Empty,
        };

        if (string.IsNullOrEmpty(methodName))
            return false;

        if (!ProviderExtensionAssemblyNames.TryGetValue(providerKey, out var providerAssemblyName))
            return false;

        var providerAssembly = ResolveProviderAssembly(host, providerAssemblyName);

        if (providerAssembly is null)
            return false;

        foreach (var exported in ReflectionToolkit.EnumerateLoadableExportedTypes(providerAssembly))
        foreach (var staticMethodCandidate in exported.GetMethods(BindingFlags.Static | BindingFlags.Public
                                                                   | BindingFlags.NonPublic))
        {
            if (!staticMethodCandidate.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false))
                continue;

            if (!string.Equals(staticMethodCandidate.Name, methodName, StringComparison.Ordinal))
                continue;

            var parametersDetailed = staticMethodCandidate.GetParameters();

            if (parametersDetailed.Length < 2)
                continue;

            if (!parametersDetailed[0].ParameterType.IsAssignableFrom(closedBuilderInstance.GetType()))
                continue;

            if (parametersDetailed[1].ParameterType != typeof(string))
                continue;

            var invokeArguments = new object?[parametersDetailed.Length];
            invokeArguments[0] = closedBuilderInstance;
            invokeArguments[1] = connectionString;

            staticMethodCandidate.Invoke(null, invokeArguments);

            return true;
        }

        return false;
    }

    private static Assembly? ResolveProviderAssembly(WorkspaceHost host, string providerAssemblyName)
        => LoadWorkspaceAssembly(host, providerAssemblyName);
}
