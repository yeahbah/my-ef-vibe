using System.Reflection;
using MyEfVibe.Workspace;

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
