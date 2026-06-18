using System.Reflection;
using MyEfVibe.Workspace;

namespace MyEfVibe;

internal sealed class PomeloMySqlProviderConfigurator : IProviderConfigurator
{
    public bool CanHandle(ProviderDescriptor descriptor)
    {
        return descriptor.KnownProvider is MyEfVibeProvider.MySql or MyEfVibeProvider.MariaDb
               && (string.Equals(
                       descriptor.PackageId,
                       "Pomelo.EntityFrameworkCore.MySql",
                       StringComparison.OrdinalIgnoreCase)
                   || string.Equals(
                       descriptor.PackageId,
                       "Microting.EntityFrameworkCore.MySql",
                       StringComparison.OrdinalIgnoreCase));
    }

    public bool TryConfigure(
        ProviderDescriptor descriptor,
        WorkspaceHost host,
        Assembly providerAssembly,
        object closedBuilderInstance,
        string connectionString)
    {
        return descriptor.KnownProvider is { } knownProvider
               && PomeloMySqlConfigurator.TryInvokeUseMySql(
                   providerAssembly,
                   closedBuilderInstance,
                   connectionString,
                   knownProvider);
    }
}
