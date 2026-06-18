using System.Reflection;
using MyEfVibe.Workspace;

namespace MyEfVibe;

/// <summary>
///     Ensures the Couchbase SDK cluster (and bucket) finish bootstrapping before EF queries run.
/// </summary>
internal static class CouchbaseClusterBootstrapper
{
    private const string ClusterProviderTypeName =
        "Couchbase.Extensions.DependencyInjection.IClusterProvider";

    private static readonly TimeSpan BootstrapTimeout = TimeSpan.FromSeconds(60);

    internal static void TryBootstrap(object dbContextInstance, WorkspaceHost host)
    {
        if (!ShouldBootstrap(host))
        {
            return;
        }

        try
        {
            TryBootstrapAsync(dbContextInstance, host, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "Couchbase cluster failed to bootstrap before the REPL session started."
                + " Ensure the cluster is reachable and Couchbase settings on `-s` are correct."
                + $"{Environment.NewLine}{DescribeFailure(failure)}",
                failure);
        }
    }

    internal static async Task TryBootstrapAsync(
        object dbContextInstance,
        WorkspaceHost host,
        CancellationToken cancellationToken)
    {
        if (!ShouldBootstrap(host))
        {
            return;
        }

        var settings = host.ActiveCouchbaseSettings;

        if (settings is null
            && !CouchbaseSettingsResolver.TryResolve(host.StartupProjectPath, out settings))
        {
            return;
        }

        var cluster = await TryGetClusterAsync(dbContextInstance, host, cancellationToken)
                      ?? await ConnectClusterFromSettingsAsync(settings, host, cancellationToken);

        if (cluster is null)
        {
            return;
        }

        await WaitUntilReadyAsync(cluster, cancellationToken);

        var bucketName = settings.BucketName;

        if (string.IsNullOrWhiteSpace(bucketName))
        {
            var database = dbContextInstance.GetType().GetProperty("Database")?.GetValue(dbContextInstance);

            if (database is not null)
            {
                bucketName = TryReadBucketNameFromConnectionString(database);
            }
        }

        if (!string.IsNullOrWhiteSpace(bucketName))
        {
            await WaitForBucketAsync(cluster, bucketName, cancellationToken);
        }

        var databaseFacade = dbContextInstance.GetType().GetProperty("Database")?.GetValue(dbContextInstance);

        if (databaseFacade is not null)
        {
            await TryOpenDatabaseAsync(databaseFacade, cancellationToken);
        }
    }

    private static string DescribeFailure(Exception failure)
    {
        if (failure is AggregateException aggregate)
        {
            return string.Join(
                Environment.NewLine,
                aggregate.Flatten().InnerExceptions.Select(static inner => inner.Message));
        }

        return failure.Message;
    }

    private static string? TryReadBucketNameFromConnectionString(object databaseFacade)
    {
        var connectionString = TryGetConnectionString(databaseFacade);

        return string.IsNullOrWhiteSpace(connectionString)
            ? null
            : TryReadBucketQueryParameter(connectionString);
    }

    private static string? TryGetConnectionString(object databaseFacade)
    {
        foreach (var method in databaseFacade.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!string.Equals(method.Name, "GetConnectionString", StringComparison.Ordinal)
                || method.GetParameters().Length != 0)
            {
                continue;
            }

            if (method.Invoke(databaseFacade, null) is string connectionString
                && !string.IsNullOrWhiteSpace(connectionString))
            {
                return connectionString;
            }
        }

        return null;
    }

    private static string? TryReadBucketQueryParameter(string connectionString)
    {
        var queryIndex = connectionString.IndexOf('?', StringComparison.Ordinal);

        if (queryIndex < 0 || queryIndex >= connectionString.Length - 1)
        {
            return null;
        }

        var query = connectionString[(queryIndex + 1)..];

        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split('=', 2);

            if (parts.Length == 2
                && string.Equals(parts[0], "bucket", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(parts[1]))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    private static bool ShouldBootstrap(WorkspaceHost host)
    {
        return host.ActiveProviderDescriptor?.IsCouchbase == true
               || host.ActiveCouchbaseSettings is not null;
    }

    private static async Task TryOpenDatabaseAsync(object databaseFacade, CancellationToken cancellationToken)
    {
        var openAsync = databaseFacade.GetType().GetMethod(
            "OpenAsync",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            [typeof(CancellationToken)],
            modifiers: null);

        if (openAsync is null)
        {
            return;
        }

        await AwaitInvocationAsync(openAsync.Invoke(databaseFacade, [cancellationToken]), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<object?> TryGetClusterAsync(
        object dbContextInstance,
        WorkspaceHost host,
        CancellationToken cancellationToken)
    {
        var serviceProvider = EfInfrastructureServiceProviderAccessor.TryGetServiceProvider(dbContextInstance);

        if (serviceProvider is null)
        {
            return null;
        }

        var clusterProvider = TryResolveClusterProvider(dbContextInstance, host, serviceProvider);

        if (clusterProvider is null)
        {
            return null;
        }

        var getClusterAsync = clusterProvider.GetType().GetMethod(
            "GetClusterAsync",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);

        if (getClusterAsync is null)
        {
            return null;
        }

        return await AwaitInvocationAsync(
            getClusterAsync.Invoke(clusterProvider, null),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<object?> ConnectClusterFromSettingsAsync(
        CouchbaseSettings settings,
        WorkspaceHost host,
        CancellationToken cancellationToken)
    {
        host.EnsureProviderDependenciesLoaded(
            host.ActiveProviderDescriptor
            ?? ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.Couchbase));

        var clusterType = ResolveType("Couchbase.ICluster", host)
                          ?? ResolveType("Couchbase.Cluster", host);

        if (clusterType is null)
        {
            return null;
        }

        var clusterOptions = CreateClusterOptions(settings, host);

        if (clusterOptions is null)
        {
            return null;
        }

        foreach (var connectAsync in clusterType.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (!string.Equals(connectAsync.Name, "ConnectAsync", StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = connectAsync.GetParameters();

            if (parameters.Length != 1 || !parameters[0].ParameterType.IsInstanceOfType(clusterOptions))
            {
                continue;
            }

            var cluster = await AwaitInvocationAsync(
                connectAsync.Invoke(null, [clusterOptions]),
                cancellationToken).ConfigureAwait(false);

            if (cluster is not null)
            {
                return cluster;
            }
        }

        return null;
    }

    private static object? CreateClusterOptions(CouchbaseSettings settings, WorkspaceHost host)
    {
        var clusterOptionsType = ResolveType("Couchbase.ClusterOptions", host);

        if (clusterOptionsType is null)
        {
            return null;
        }

        var clusterOptions = Activator.CreateInstance(clusterOptionsType);

        if (clusterOptions is null)
        {
            return null;
        }

        if (!TryInvokeFluent(clusterOptions, "WithConnectionString", settings.ConnectionString)
            || !TryInvokeFluent(clusterOptions, "WithCredentials", settings.Username, settings.Password))
        {
            return null;
        }

        return clusterOptions;
    }

    private static bool TryInvokeFluent(object instance, string methodName, params object?[] arguments)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);

        if (method is null)
        {
            return false;
        }

        method.Invoke(instance, arguments);

        return true;
    }

    private static object? TryResolveClusterProvider(
        object dbContextInstance,
        WorkspaceHost host,
        object serviceProvider)
    {
        foreach (var serviceType in EnumerateClusterProviderTypes(dbContextInstance, host))
        {
            var provider = EfInfrastructureServiceProviderAccessor.TryGetService(serviceProvider, serviceType);

            if (provider is not null)
            {
                return provider;
            }
        }

        return null;
    }

    private static IEnumerable<Type> EnumerateClusterProviderTypes(
        object dbContextInstance,
        WorkspaceHost host)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assembly in EnumerateCandidateAssemblies(dbContextInstance, host))
        {
            var type = assembly.GetType(ClusterProviderTypeName, false);

            if (type is null)
            {
                continue;
            }

            var key = type.AssemblyQualifiedName ?? type.FullName ?? type.Name;

            if (seen.Add(key))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<Assembly> EnumerateCandidateAssemblies(
        object dbContextInstance,
        WorkspaceHost host)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (seen.Add(dbContextInstance.GetType().Assembly.FullName ?? string.Empty))
        {
            yield return dbContextInstance.GetType().Assembly;
        }

        foreach (var assemblyName in new[]
                 {
                     "Couchbase.Extensions.DependencyInjection",
                     "Couchbase.EntityFrameworkCore",
                     "Couchbase.NetClient"
                 })
        {
            var assembly = host.LoadAssembly(assemblyName);

            if (assembly is not null && seen.Add(assembly.FullName ?? assembly.GetName().Name ?? assemblyName))
            {
                yield return assembly;
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var key = assembly.FullName ?? assembly.GetName().Name ?? string.Empty;

            if (seen.Add(key))
            {
                yield return assembly;
            }
        }
    }

    private static Type? ResolveType(string fullName, WorkspaceHost host)
    {
        foreach (var assemblyName in new[] { "Couchbase.NetClient", "Couchbase.Extensions.DependencyInjection" })
        {
            var assembly = host.LoadAssembly(assemblyName);
            var type = assembly?.GetType(fullName, false);

            if (type is not null)
            {
                return type;
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(fullName, false);

            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }

    private static async Task WaitUntilReadyAsync(object cluster, CancellationToken cancellationToken)
    {
        var waitMethod = FindWaitUntilReadyAsyncMethod(cluster.GetType());

        if (waitMethod is null)
        {
            return;
        }

        await AwaitInvocationAsync(
            waitMethod.Invoke(cluster, CreateWaitUntilReadyArguments(waitMethod)),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task WaitForBucketAsync(
        object cluster,
        string bucketName,
        CancellationToken cancellationToken)
    {
        var bucketAsync = cluster.GetType().GetMethod(
            "BucketAsync",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            [typeof(string)],
            modifiers: null);

        if (bucketAsync is null)
        {
            return;
        }

        var bucket = await AwaitInvocationAsync(
            bucketAsync.Invoke(cluster, [bucketName]),
            cancellationToken).ConfigureAwait(false);

        if (bucket is null)
        {
            return;
        }

        var waitMethod = FindWaitUntilReadyAsyncMethod(bucket.GetType());

        if (waitMethod is null)
        {
            return;
        }

        await AwaitInvocationAsync(
            waitMethod.Invoke(bucket, CreateWaitUntilReadyArguments(waitMethod)),
            cancellationToken).ConfigureAwait(false);
    }

    private static object?[] CreateWaitUntilReadyArguments(MethodInfo waitMethod)
    {
        var parameters = waitMethod.GetParameters();
        var arguments = new object?[parameters.Length];
        arguments[0] = BootstrapTimeout;

        for (var index = 1; index < parameters.Length; index++)
        {
            arguments[index] = parameters[index].HasDefaultValue
                ? parameters[index].DefaultValue
                : null;
        }

        return arguments;
    }

    private static MethodInfo? FindWaitUntilReadyAsyncMethod(Type targetType)
    {
        foreach (var method in targetType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!string.Equals(method.Name, "WaitUntilReadyAsync", StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = method.GetParameters();

            if (parameters.Length >= 1 && parameters[0].ParameterType == typeof(TimeSpan))
            {
                return method;
            }
        }

        return null;
    }

    private static async Task<object?> AwaitInvocationAsync(
        object? invocationResult,
        CancellationToken cancellationToken)
    {
        if (invocationResult is null)
        {
            return null;
        }

        if (invocationResult is Task task)
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(false);

            return TryReadTaskResult(task);
        }

        var asTask = invocationResult.GetType().GetMethod(
            "AsTask",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);

        if (asTask?.Invoke(invocationResult, null) is not Task convertedTask)
        {
            return invocationResult;
        }

        await convertedTask.WaitAsync(cancellationToken).ConfigureAwait(false);

        return TryReadTaskResult(convertedTask);
    }

    private static object? TryReadTaskResult(Task task)
    {
        return task.GetType().IsGenericType
            ? task.GetType().GetProperty("Result")?.GetValue(task)
            : null;
    }
}
