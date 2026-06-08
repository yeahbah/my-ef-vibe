using System.Reflection;

namespace MyEfVibe;

internal static class ProviderConfiguratorRegistry
{
    private static readonly IProviderConfigurator[] Configurators =
    [
        new PomeloMySqlProviderConfigurator()
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
}
