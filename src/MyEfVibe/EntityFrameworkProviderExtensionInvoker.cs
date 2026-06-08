using System.Reflection;
using System.Runtime.CompilerServices;

namespace MyEfVibe;

internal static class EntityFrameworkProviderExtensionInvoker
{
    private static readonly string[] ExcludedExtensionMethodNames =
    [
        "UseInternal",
        "UseModel",
        "UseChangeTrackingProxies",
        "UseLazyLoadingProxies"
    ];

    internal static bool TryInvoke(
        WorkspaceHost host,
        ProviderDescriptor descriptor,
        object closedBuilderInstance,
        string connectionString)
    {
        var providerAssembly = host.LoadAssembly(descriptor.ProviderAssemblyName);

        if (providerAssembly is null)
        {
            return false;
        }

        if (ProviderConfiguratorRegistry.TryConfigure(
                descriptor,
                host,
                providerAssembly,
                closedBuilderInstance,
                connectionString))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(descriptor.ExtensionMethodName))
        {
            if (descriptor.KnownProvider is { } knownProvider
                && ProviderOptionsConfigurator.TryInvokeUseProviderWithOptions(
                    host,
                    providerAssembly,
                    closedBuilderInstance,
                    connectionString,
                    descriptor.ExtensionMethodName,
                    knownProvider))
            {
                return true;
            }

            if (TryInvokeNamedExtension(
                    providerAssembly,
                    closedBuilderInstance,
                    connectionString,
                    descriptor.ExtensionMethodName))
            {
                return true;
            }
        }

        return TryInvokeDiscoveredExtension(
            providerAssembly,
            closedBuilderInstance,
            connectionString,
            descriptor);
    }

    internal static string DescribeInvokeFailure(ProviderDescriptor descriptor)
    {
        return "Could not invoke a `Use*` extension for `"
               + descriptor.PackageId
               + "`. Ensure the provider package is restored and exposes "
               + "`Use*(DbContextOptionsBuilder, string)`. "
               + "Providers that need extra configuration may require a registered configurator.";
    }

    private static bool TryInvokeNamedExtension(
        Assembly providerAssembly,
        object closedBuilderInstance,
        string connectionString,
        string methodName)
    {
        foreach (var exported in ReflectionToolkit.EnumerateLoadableExportedTypes(providerAssembly))
        foreach (var staticMethodCandidate in exported.GetMethods(BindingFlags.Static | BindingFlags.Public
                     | BindingFlags.NonPublic))
        {
            if (!staticMethodCandidate.IsDefined(typeof(ExtensionAttribute), false))
            {
                continue;
            }

            if (!string.Equals(staticMethodCandidate.Name, methodName, StringComparison.Ordinal))
            {
                continue;
            }

            if (TryInvokeTwoParameterExtension(staticMethodCandidate, closedBuilderInstance, connectionString))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryInvokeDiscoveredExtension(
        Assembly providerAssembly,
        object closedBuilderInstance,
        string connectionString,
        ProviderDescriptor descriptor)
    {
        var candidates = new List<MethodInfo>();

        foreach (var exported in ReflectionToolkit.EnumerateLoadableExportedTypes(providerAssembly))
        foreach (var staticMethodCandidate in exported.GetMethods(BindingFlags.Static | BindingFlags.Public
                     | BindingFlags.NonPublic))
        {
            if (!staticMethodCandidate.IsDefined(typeof(ExtensionAttribute), false))
            {
                continue;
            }

            if (!IsCandidateUseExtension(staticMethodCandidate, closedBuilderInstance))
            {
                continue;
            }

            candidates.Add(staticMethodCandidate);
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        var ordered = candidates
            .OrderByDescending(method => ScoreDiscoveredExtension(method, descriptor))
            .ThenBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();

        return TryInvokeTwoParameterExtension(ordered[0], closedBuilderInstance, connectionString);
    }

    private static bool IsCandidateUseExtension(MethodInfo method, object closedBuilderInstance)
    {
        if (!method.Name.StartsWith("Use", StringComparison.Ordinal)
            || method.Name.Length <= 3)
        {
            return false;
        }

        if (ExcludedExtensionMethodNames.Any(excluded =>
                string.Equals(method.Name, excluded, StringComparison.Ordinal)))
        {
            return false;
        }

        var parameters = method.GetParameters();

        if (parameters.Length != 2)
        {
            return false;
        }

        if (!parameters[0].ParameterType.IsAssignableFrom(closedBuilderInstance.GetType()))
        {
            return false;
        }

        return parameters[1].ParameterType == typeof(string);
    }

    private static int ScoreDiscoveredExtension(MethodInfo method, ProviderDescriptor descriptor)
    {
        var score = 0;
        var methodName = method.Name;
        var packageId = descriptor.PackageId;

        if (packageId.Contains(methodName[3..], StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (string.Equals(methodName, $"Use{descriptor.ProviderAssemblyName}", StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        foreach (var segment in packageId.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(methodName, $"Use{segment}", StringComparison.OrdinalIgnoreCase))
            {
                score += 25;
            }
        }

        return score;
    }

    private static bool TryInvokeTwoParameterExtension(
        MethodInfo staticMethodCandidate,
        object closedBuilderInstance,
        string connectionString)
    {
        var parametersDetailed = staticMethodCandidate.GetParameters();

        if (parametersDetailed.Length != 2
            || !parametersDetailed[0].ParameterType.IsAssignableFrom(closedBuilderInstance.GetType())
            || parametersDetailed[1].ParameterType != typeof(string))
        {
            return false;
        }

        staticMethodCandidate.Invoke(null, [closedBuilderInstance, connectionString]);

        return true;
    }
}
