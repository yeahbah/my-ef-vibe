using System.Reflection;
using System.Runtime.CompilerServices;

namespace MyEfVibe;

internal static class CouchbaseProviderConfigurator
{
    internal static bool TryInvokeUseCouchbase(
        Assembly providerAssembly,
        object closedBuilderInstance,
        CouchbaseSettings settings)
    {
        var clusterOptions = TryCreateClusterOptions(providerAssembly, settings);

        if (clusterOptions is null)
        {
            return false;
        }

        foreach (var exported in ReflectionToolkit.EnumerateLoadableExportedTypes(providerAssembly))
        foreach (var staticMethodCandidate in exported.GetMethods(BindingFlags.Static | BindingFlags.Public
                     | BindingFlags.NonPublic))
        {
            if (!staticMethodCandidate.IsDefined(typeof(ExtensionAttribute), false))
            {
                continue;
            }

            if (!string.Equals(staticMethodCandidate.Name, "UseCouchbase", StringComparison.Ordinal))
            {
                continue;
            }

            if (TryInvokeUseCouchbase(staticMethodCandidate, closedBuilderInstance, clusterOptions, settings))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryInvokeUseCouchbase(
        MethodInfo method,
        object closedBuilderInstance,
        object clusterOptions,
        CouchbaseSettings settings)
    {
        var parameters = method.GetParameters();

        if (parameters.Length is not (2 or 3))
        {
            return false;
        }

        if (!parameters[0].ParameterType.IsAssignableFrom(closedBuilderInstance.GetType()))
        {
            return false;
        }

        if (!parameters[1].ParameterType.IsInstanceOfType(clusterOptions))
        {
            return false;
        }

        if (parameters.Length == 2)
        {
            method.Invoke(null, [closedBuilderInstance, clusterOptions]);

            return true;
        }

        if (!parameters[2].ParameterType.IsGenericType
            || parameters[2].ParameterType.GetGenericTypeDefinition() != typeof(Action<>))
        {
            return false;
        }

        var configureDelegate = CreateConfigureDelegate(parameters[2].ParameterType, settings);

        method.Invoke(null, [closedBuilderInstance, clusterOptions, configureDelegate]);

        return true;
    }

    private static Delegate CreateConfigureDelegate(Type actionType, CouchbaseSettings settings)
    {
        var configureMethod = typeof(CouchbaseProviderConfigurator).GetMethod(
            nameof(ConfigureCouchbaseOptions),
            BindingFlags.Static | BindingFlags.NonPublic)!;

        return Delegate.CreateDelegate(actionType, settings, configureMethod);
    }

    private static void ConfigureCouchbaseOptions(CouchbaseSettings settings, object optionsBuilder)
    {
        var optionsType = optionsBuilder.GetType();
        SetProperty(optionsType, optionsBuilder, "Bucket", settings.BucketName);
        SetProperty(optionsType, optionsBuilder, "Scope", settings.ScopeName);

        if (!string.IsNullOrWhiteSpace(settings.CollectionName))
        {
            SetProperty(optionsType, optionsBuilder, "Collection", settings.CollectionName);
        }
    }

    private static void SetProperty(Type optionsType, object instance, string propertyName, string value)
    {
        var property = optionsType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);

        property?.SetValue(instance, value);
    }

    private static object? TryCreateClusterOptions(Assembly providerAssembly, CouchbaseSettings settings)
    {
        var clusterOptionsType = providerAssembly.GetType("Couchbase.ClusterOptions", false)
                                 ?? LoadTypeFromReferencedAssemblies(providerAssembly, "Couchbase.ClusterOptions");

        if (clusterOptionsType is null)
        {
            return null;
        }

        var clusterOptions = Activator.CreateInstance(clusterOptionsType);

        if (clusterOptions is null)
        {
            return null;
        }

        if (!TryInvokeFluent(clusterOptions, "WithConnectionString", settings.ConnectionString))
        {
            return null;
        }

        if (!TryInvokeFluent(clusterOptions, "WithCredentials", settings.Username, settings.Password))
        {
            return null;
        }

        return clusterOptions;
    }

    private static Type? LoadTypeFromReferencedAssemblies(Assembly providerAssembly, string fullName)
    {
        foreach (var referenceName in providerAssembly.GetReferencedAssemblies())
        {
            if (!referenceName.Name?.StartsWith("Couchbase", StringComparison.OrdinalIgnoreCase) == true)
            {
                continue;
            }

            try
            {
                var referenceAssembly = Assembly.Load(referenceName);
                var type = referenceAssembly.GetType(fullName, false);

                if (type is not null)
                {
                    return type;
                }
            }
            catch (Exception)
            {
            }
        }

        return null;
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
}
