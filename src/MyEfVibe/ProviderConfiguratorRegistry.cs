using System.Reflection;
using MyEfVibe.Workspace;

namespace MyEfVibe;

internal static class ProviderConfiguratorRegistry
{
    private static readonly IProviderConfigurator[] Configurators =
    [
        new PomeloMySqlProviderConfigurator(),
        new CouchbaseEntityFrameworkProviderConfigurator()
    ];

    private static readonly ICouchbaseProviderConfigurator[] CouchbaseConfigurators =
    [
        new CouchbaseSettingsProviderConfigurator()
    ];

    internal static bool TryConfigure(
        ProviderDescriptor descriptor,
        WorkspaceHost host,
        Assembly providerAssembly,
        object closedBuilderInstance,
        string connectionString)
    {
        foreach (var configurator in Configurators)
        {
            if (!configurator.CanHandle(descriptor))
            {
                continue;
            }

            if (configurator.TryConfigure(
                    descriptor,
                    host,
                    providerAssembly,
                    closedBuilderInstance,
                    connectionString))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TryConfigureCouchbase(
        ProviderDescriptor descriptor,
        WorkspaceHost host,
        Assembly providerAssembly,
        object closedBuilderInstance,
        CouchbaseSettings settings)
    {
        foreach (var configurator in CouchbaseConfigurators)
        {
            if (!configurator.CanHandle(descriptor))
            {
                continue;
            }

            if (configurator.TryConfigure(
                    descriptor,
                    host,
                    providerAssembly,
                    closedBuilderInstance,
                    settings))
            {
                return true;
            }
        }

        return false;
    }
}
