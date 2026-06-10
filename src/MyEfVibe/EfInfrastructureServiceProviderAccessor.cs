using System.Reflection;

namespace MyEfVibe;

internal static class EfInfrastructureServiceProviderAccessor
{
    internal static object? TryGetServiceProvider(object dbContextInstance)
    {
        foreach (var iface in dbContextInstance.GetType().GetInterfaces())
        {
            if (iface.IsGenericType
                && string.Equals(
                    iface.GetGenericTypeDefinition().FullName,
                    "Microsoft.EntityFrameworkCore.Infrastructure.IInfrastructure`1",
                    StringComparison.Ordinal)
                && string.Equals(
                    iface.GetGenericArguments()[0].FullName,
                    typeof(IServiceProvider).FullName,
                    StringComparison.Ordinal))
            {
                return iface.GetProperty("Instance")?.GetValue(dbContextInstance);
            }

            if (!iface.IsGenericType
                && string.Equals(iface.FullName, "Microsoft.EntityFrameworkCore.Infrastructure.IInfrastructure", StringComparison.Ordinal))
            {
                return iface.GetProperty("Instance")?.GetValue(dbContextInstance);
            }
        }

        var database = dbContextInstance.GetType().GetProperty("Database")?.GetValue(dbContextInstance);

        if (database is null)
        {
            return null;
        }

        foreach (var method in database.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!string.Equals(method.Name, "GetInfrastructure", StringComparison.Ordinal)
                || method.GetParameters().Length != 0)
            {
                continue;
            }

            return method.Invoke(database, null);
        }

        return null;
    }

    internal static object? TryGetService(object serviceProvider, Type serviceType)
    {
        if (serviceProvider is IServiceProvider provider)
        {
            return provider.GetService(serviceType);
        }

        foreach (var method in serviceProvider.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!string.Equals(method.Name, "GetService", StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = method.GetParameters();

            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(Type))
            {
                continue;
            }

            return method.Invoke(serviceProvider, [serviceType]);
        }

        return null;
    }
}
