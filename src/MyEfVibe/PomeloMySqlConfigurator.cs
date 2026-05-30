using System.Reflection;
using System.Runtime.CompilerServices;

namespace MyEfVibe;

/// <summary>
///     Pomelo's <c>UseMySql</c> requires an explicit <c>ServerVersion</c>; unlike SQL Server/Npgsql two-parameter
///     overloads.
/// </summary>
internal static class PomeloMySqlConfigurator
{
    internal static bool TryInvokeUseMySql(
        Assembly providerAssembly,
        object closedBuilderInstance,
        string connectionString,
        MyEfVibeProvider providerKey)
    {
        var serverVersion = TryCreateServerVersion(providerAssembly, providerKey);

        if (serverVersion is null)
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

            if (!string.Equals(staticMethodCandidate.Name, "UseMySql", StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = staticMethodCandidate.GetParameters();

            if (parameters.Length is < 3 or > 4)
            {
                continue;
            }

            if (!parameters[0].ParameterType.IsAssignableFrom(closedBuilderInstance.GetType()))
            {
                continue;
            }

            if (parameters[1].ParameterType != typeof(string))
            {
                continue;
            }

            if (!IsServerVersionType(parameters[2].ParameterType))
            {
                continue;
            }

            if (!parameters[2].ParameterType.IsInstanceOfType(serverVersion))
            {
                continue;
            }

            var invokeArguments = parameters.Length switch
            {
                3 => new[] { closedBuilderInstance, connectionString, serverVersion },
                4 => new[] { closedBuilderInstance, connectionString, serverVersion, null },
                _ => null
            };

            if (invokeArguments is null)
            {
                continue;
            }

            staticMethodCandidate.Invoke(null, invokeArguments);

            return true;
        }

        return false;
    }

    private static bool IsServerVersionType(Type parameterType)
    {
        return string.Equals(parameterType.Name, "ServerVersion", StringComparison.Ordinal)
               || parameterType.FullName?.EndsWith(".ServerVersion", StringComparison.Ordinal) == true;
    }

    private static object? TryCreateServerVersion(Assembly providerAssembly, MyEfVibeProvider providerKey)
    {
        var versionTypeName = providerKey == MyEfVibeProvider.MariaDb
            ? "MariaDbServerVersion"
            : "MySqlServerVersion";

        var serverVersionType = providerAssembly.GetType(
            $"Microsoft.EntityFrameworkCore.{versionTypeName}",
            false);

        serverVersionType ??= providerAssembly.GetType(
            $"Pomelo.EntityFrameworkCore.MySql.Infrastructure.{versionTypeName}",
            false);

        serverVersionType ??= ReflectionToolkit.EnumerateLoadableExportedTypes(providerAssembly)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, versionTypeName, StringComparison.Ordinal));

        if (serverVersionType is not null)
        {
            var defaultVersion = providerKey == MyEfVibeProvider.MariaDb
                ? new Version(11, 4, 0)
                : new Version(8, 0, 0);

            var versionCtor = serverVersionType.GetConstructor([typeof(Version)]);

            if (versionCtor is not null)
            {
                return versionCtor.Invoke([defaultVersion]);
            }

            foreach (var ctor in serverVersionType.GetConstructors())
            {
                var ctorParameters = ctor.GetParameters();

                if (ctorParameters.Length == 1 && ctorParameters[0].ParameterType == typeof(string))
                {
                    return ctor.Invoke([defaultVersion.ToString()]);
                }
            }
        }

        var serverVersionBase = providerAssembly.GetType(
            "Microsoft.EntityFrameworkCore.ServerVersion",
            false);

        if (serverVersionBase is null)
        {
            return null;
        }

        foreach (var parseMethod in serverVersionBase.GetMethods(BindingFlags.Static | BindingFlags.Public))
        {
            if (!string.Equals(parseMethod.Name, "Parse", StringComparison.Ordinal))
            {
                continue;
            }

            var parseParameters = parseMethod.GetParameters();

            if (parseParameters.Length != 1 || parseParameters[0].ParameterType != typeof(string))
            {
                continue;
            }

            var versionString = providerKey == MyEfVibeProvider.MariaDb ? "11.4.0-mariadb" : "8.0.0-mysql";

            return parseMethod.Invoke(null, [versionString]);
        }

        return null;
    }
}