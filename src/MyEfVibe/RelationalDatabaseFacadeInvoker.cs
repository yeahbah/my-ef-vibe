using System.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MyEfVibe;

internal static class RelationalDatabaseFacadeInvoker
{
    private const string ExtensionsTypeName = "Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions";

    internal static bool TryGetDbConnection(
        object databaseFacade,
        IEnumerable<Assembly> inspectionAssemblies,
        out DbConnection? connection)
    {
        if (TryGetDbConnectionCore(databaseFacade, inspectionAssemblies, out connection))
            return true;

        return TryGetDbConnectionCore(
            databaseFacade,
            AppDomain.CurrentDomain.GetAssemblies(),
            out connection);
    }

    private static bool TryGetDbConnectionCore(
        object databaseFacade,
        IEnumerable<Assembly> inspectionAssemblies,
        out DbConnection? connection)
    {
        connection = null;

        foreach (var assembly in inspectionAssemblies)
        {
            var extensionsType = assembly.GetType(ExtensionsTypeName, throwOnError: false);

            if (extensionsType is null)
                continue;

            foreach (var method in extensionsType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, "GetDbConnection", StringComparison.Ordinal))
                    continue;

                if (!method.IsDefined(typeof(ExtensionAttribute), inherit: false))
                    continue;

                var parameters = method.GetParameters();

                if (parameters.Length != 1)
                    continue;

                if (!parameters[0].ParameterType.IsAssignableFrom(databaseFacade.GetType()))
                    continue;

                try
                {
                    connection = method.Invoke(null, new[] { databaseFacade }) as DbConnection;

                    if (connection is not null)
                        return true;
                }
                catch
                {
                }
            }

            foreach (var exported in ReflectionToolkit.EnumerateLoadableExportedTypes(assembly))
            {
                if (!string.Equals(exported.FullName, ExtensionsTypeName, StringComparison.Ordinal))
                    continue;

                foreach (var method in exported.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!string.Equals(method.Name, "GetDbConnection", StringComparison.Ordinal))
                        continue;

                    var parameters = method.GetParameters();

                    if (parameters.Length != 1)
                        continue;

                    if (!parameters[0].ParameterType.IsAssignableFrom(databaseFacade.GetType()))
                        continue;

                    try
                    {
                        connection = method.Invoke(null, new[] { databaseFacade }) as DbConnection;

                        if (connection is not null)
                            return true;
                    }
                    catch
                    {
                    }
                }
            }
        }

        return false;
    }
}
