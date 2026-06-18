using System.Reflection;
using MyEfVibe.Workspace;

namespace MyEfVibe;

internal sealed class CouchbaseEntityFrameworkProviderConfigurator : IProviderConfigurator
{
    public bool CanHandle(ProviderDescriptor descriptor)
    {
        return descriptor.IsCouchbase;
    }

    public bool TryConfigure(
        ProviderDescriptor descriptor,
        WorkspaceHost host,
        Assembly providerAssembly,
        object closedBuilderInstance,
        string connectionString)
    {
        return false;
    }
}

internal interface ICouchbaseProviderConfigurator
{
    bool CanHandle(ProviderDescriptor descriptor);

    bool TryConfigure(
        ProviderDescriptor descriptor,
        WorkspaceHost host,
        Assembly providerAssembly,
        object closedBuilderInstance,
        CouchbaseSettings settings);
}

internal sealed class CouchbaseSettingsProviderConfigurator : ICouchbaseProviderConfigurator
{
    public bool CanHandle(ProviderDescriptor descriptor)
    {
        return descriptor.IsCouchbase;
    }

    public bool TryConfigure(
        ProviderDescriptor descriptor,
        WorkspaceHost host,
        Assembly providerAssembly,
        object closedBuilderInstance,
        CouchbaseSettings settings)
    {
        return CouchbaseProviderConfigurator.TryInvokeUseCouchbase(
            providerAssembly,
            closedBuilderInstance,
            settings);
    }
}
