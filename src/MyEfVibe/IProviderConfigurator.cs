using System.Reflection;

namespace MyEfVibe;

internal interface IProviderConfigurator
{
    bool CanHandle(ProviderDescriptor descriptor);

    bool TryConfigure(
        ProviderDescriptor descriptor,
        WorkspaceHost host,
        Assembly providerAssembly,
        object closedBuilderInstance,
        string connectionString);
}
