using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using MyEfVibe.Workspace;

namespace MyEfVibe;

internal static class DbContextActivator
{
    internal static Type ResolveContextType(
        WorkspaceHost host,
        string? contextFullName,
        bool allowInteractiveSelection = true)
    {
        var discoveredDbContextTypes =
            DiscoverDbContextConcreteTypes(host, contextFullName)
                .OrderBy(static t => t.FullName, StringComparer.Ordinal)
                .ToArray();

        if (discoveredDbContextTypes.Length == 0)
        {
            throw new InvalidOperationException(BuildNoDbContextDiscoveredMessage(host, contextFullName));
        }

        return SelectDbContextType(
            discoveredDbContextTypes,
            contextFullName,
            allowInteractiveSelection);
    }

    internal static object ResolveInstance(
        WorkspaceHost host,
        string? contextFullName,
        string? connectionString,
        ProviderDescriptor? explicitProvider,
        bool allowInteractiveSelection = true)
    {
        var providerDescriptor = explicitProvider;
        MyEfVibeProvider? provider = explicitProvider?.KnownProvider;

        // SQLitePCL must be initialized before EF discovery or any Microsoft.Data.Sqlite load.
        if (!string.IsNullOrWhiteSpace(connectionString)
            && (providerDescriptor?.IsSqlite == true
                || provider == MyEfVibeProvider.Sqlite
                || SqliteConnectionStringNormalizer.LooksLikeSqliteConnection(connectionString)))
        {
            connectionString = SqliteConnectionStringNormalizer.Normalize(
                connectionString,
                host.StartupProjectPath,
                host.OutputDirectory);
            provider ??= MyEfVibeProvider.Sqlite;
            providerDescriptor ??= ProviderDescriptor.TryFromKnownProvider(MyEfVibeProvider.Sqlite);
        }

        providerDescriptor ??= EntityFrameworkProviderDiscovery.TryDiscoverFromProject(host.ProjectPath);
        provider ??= providerDescriptor?.KnownProvider;

        if (!CouchbaseEntityFrameworkCompatibility.TryValidateEfCoreVersion(host.ProjectPath, out var couchbaseEfError))
        {
            throw new InvalidOperationException(couchbaseEfError);
        }

        if (providerDescriptor is not null)
        {
            host.EnsureProviderDependenciesLoaded(providerDescriptor);
        }

        var selectedDbContextType = ResolveContextType(host, contextFullName, allowInteractiveSelection);

        List<string>? designTimeFactoryErrors = null;

        // When the caller passes an explicit connection string (and optional provider override in tests),
        // build DbContextOptions first so provider naming customizers
        // are registered before any design-time factory or appsettings-based construction path.
        if (!string.IsNullOrWhiteSpace(connectionString)
            && providerDescriptor is not null
            && TryCreateUsingOptionsConstructor(
                selectedDbContextType,
                NormalizeSqlServerConnectionString(connectionString),
                providerDescriptor,
                host,
                out var explicitOptionsInstance))
        {
            TryApplyProviderHints(explicitOptionsInstance, host, provider, providerDescriptor);

            return Finish(host, providerDescriptor, explicitOptionsInstance);
        }

        var prefersDesignTimeFactoryForCouchbase =
            providerDescriptor?.IsCouchbase == true
            || provider == MyEfVibeProvider.Couchbase;

        if (prefersDesignTimeFactoryForCouchbase
            && TryCreateUsingDesignTimeFactory(
                selectedDbContextType,
                host,
                out var couchbaseDesignTimeInstance,
                ref designTimeFactoryErrors))
        {
            if (providerDescriptor is not null || provider.HasValue)
            {
                TryApplyProviderHints(
                    couchbaseDesignTimeInstance,
                    host,
                    provider,
                    providerDescriptor);
            }

            return Finish(host, providerDescriptor, couchbaseDesignTimeInstance);
        }

        if (TryCreateUsingCouchbaseConfiguration(
                host,
                ref providerDescriptor,
                ref provider,
                selectedDbContextType,
                out var couchbaseInstance))
        {
            return Finish(host, providerDescriptor, couchbaseInstance);
        }

        if (TryCreateUsingDesignTimeFactory(selectedDbContextType, host, out var designTimeInstance,
                ref designTimeFactoryErrors))
        {
            providerDescriptor ??= EntityFrameworkProviderDiscovery.TryDiscoverFromProject(host.ProjectPath);

            if (providerDescriptor is not null || provider.HasValue)
            {
                TryApplyProviderHints(
                    designTimeInstance,
                    host,
                    provider,
                    providerDescriptor);
            }

            return Finish(host, providerDescriptor, designTimeInstance);
        }

        var resolvedConnectionFromConfiguration = false;

        if (string.IsNullOrWhiteSpace(connectionString)
            && AppSettingsConnectionResolver.TryResolve(
                host.StartupProjectPath,
                host.ProjectPath,
                host.OutputDirectory,
                out var fromConfiguration,
                out var inferredProviderDescriptor))
        {
            connectionString = fromConfiguration;
            providerDescriptor ??= inferredProviderDescriptor;
            provider ??= inferredProviderDescriptor?.KnownProvider;
            resolvedConnectionFromConfiguration = true;
        }
        else if (!string.IsNullOrWhiteSpace(connectionString)
                 && (providerDescriptor?.IsSqlite == true
                     || provider == MyEfVibeProvider.Sqlite
                     || SqliteConnectionStringNormalizer.LooksLikeSqliteConnection(connectionString)))
        {
            connectionString = SqliteConnectionStringNormalizer.Normalize(
                connectionString,
                host.StartupProjectPath,
                host.OutputDirectory);
            provider ??= MyEfVibeProvider.Sqlite;
            providerDescriptor ??= ProviderDescriptor.TryFromKnownProvider(MyEfVibeProvider.Sqlite);
        }

        if (!string.IsNullOrWhiteSpace(connectionString)
            && providerDescriptor is not null
            && TryCreateUsingOptionsConstructor(
                selectedDbContextType,
                NormalizeSqlServerConnectionString(connectionString),
                providerDescriptor,
                host,
                out var optionsInstance))
        {
            TryApplyProviderHints(optionsInstance, host, provider, providerDescriptor);

            return Finish(host, providerDescriptor, optionsInstance);
        }

        if (!resolvedConnectionFromConfiguration
            && TryCreateUsingParameterlessConstructor(selectedDbContextType, out var parameterlessInstance))
        {
            providerDescriptor ??= EntityFrameworkProviderDiscovery.TryDiscoverFromProject(host.ProjectPath);

            if (providerDescriptor is not null || provider.HasValue)
            {
                TryApplyProviderHints(
                    parameterlessInstance,
                    host,
                    provider,
                    providerDescriptor);
            }

            return Finish(host, providerDescriptor, parameterlessInstance);
        }

        var failureMessage =
            "Unable to construct the DbContext automatically for this project."
            + $"{Environment.NewLine}"
            + "Typical fixes:"
            + $"{Environment.NewLine}"
            + " - Add an `IDesignTimeDbContextFactory<TContext>` in the startup project (`-s`) — efvibe builds and scans that output."
            + $"{Environment.NewLine}"
            + " - Add a public parameterless constructor on the DbContext."
            + $"{Environment.NewLine}"
            + " - Pass `--connection-string` to build `DbContextOptions<TContext>` when the EF provider is discovered from `-p` (optional packages such as NetTopologySuite are applied when referenced)."
            + $"{Environment.NewLine}"
            + " - For Couchbase, ensure the startup project (`-s`) has a `Couchbase` section (or user secrets) with connection string, credentials, bucket, and scope.";

        if (designTimeFactoryErrors is { Count: > 0 })
        {
            failureMessage +=
                $"{Environment.NewLine}{Environment.NewLine}Design-time factory attempts:"
                + $"{Environment.NewLine}"
                + string.Join(Environment.NewLine, designTimeFactoryErrors.Select(static line => $" - {line}"));
        }

        if (resolvedConnectionFromConfiguration)
        {
            failureMessage +=
                $"{Environment.NewLine}{Environment.NewLine}Configuration was read from the startup project";

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                failureMessage += ", but no connection string was found.";
            }
            else if (providerDescriptor is null)
            {
                if (CouchbaseSettingsResolver.TryResolve(host.StartupProjectPath, out _))
                {
                    failureMessage +=
                        ", but the EF project (`-p`) does not reference `Couchbase.EntityFrameworkCore`.";
                }
                else if (EntityFrameworkProviderDiscovery.TryDescribeAmbiguousProviders(
                        host.ProjectPath,
                        out var ambiguousProvidersMessage))
                {
                    failureMessage +=
                        $", but {ambiguousProvidersMessage.TrimEnd('.')}.";
                }
                else
                {
                    failureMessage +=
                        ", but the database provider could not be determined from the `-p` project. Add an EF provider package reference (for example `Microsoft.EntityFrameworkCore.SqlServer`).";
                }
            }
            else
            {
                failureMessage +=
                    ", but constructing `DbContextOptions` failed. "
                    + EntityFrameworkProviderExtensionInvoker.DescribeInvokeFailure(providerDescriptor);
            }
        }

        throw new InvalidOperationException(failureMessage);
    }

    private static Type SelectDbContextType(
        IReadOnlyList<Type> discoveredDbContextTypes,
        string? contextFullName,
        bool allowInteractiveSelection)
    {
        if (!string.IsNullOrWhiteSpace(contextFullName))
        {
            var matches = discoveredDbContextTypes
                .Where(type => ContextNameMatcher.Matches(type, contextFullName))
                .ToArray();

            return matches switch
            {
                [var single] => single,
                [] => throw new InvalidOperationException(
                    $"DbContext `{contextFullName}` was not found. Known contexts:{Environment.NewLine}"
                    + string.Join(Environment.NewLine,
                        discoveredDbContextTypes.Select(static ctx => $" - {ctx.FullName} ({ctx.Name})"))),
                _ => throw new InvalidOperationException(
                    $"DbContext `{contextFullName}` is ambiguous. Specify the full name with `-c`:{Environment.NewLine}"
                    + string.Join(Environment.NewLine,
                        matches.Select(static ctx => $" - {ctx.FullName}")))
            };
        }

        if (discoveredDbContextTypes.Count == 1)
        {
            return discoveredDbContextTypes[0];
        }

        if (allowInteractiveSelection && InteractiveSelection.CanPrompt)
        {
            return InteractiveSelection.Choose(
                "[bold]Multiple DbContext types were found. Which one should be used?[/]",
                discoveredDbContextTypes
                    .Select(type => new SelectionOption<Type>(
                        type,
                        $"{type.FullName} [grey]({type.Assembly.GetName().Name})[/]"))
                    .ToArray());
        }

        throw new InvalidOperationException(
            "Multiple DbContext types were discovered. Choose one with `-c/--context`."
            + $"{Environment.NewLine}"
            + string.Join(Environment.NewLine,
                discoveredDbContextTypes.Select(static ctx => $" - {ctx.FullName}")));
    }

    private static ImmutableArray<Type> DiscoverDbContextConcreteTypes(
        WorkspaceHost host,
        string? preferredContextFullName)
    {
        host.EnsureEntityFrameworkCoreLoaded();

        if (!string.IsNullOrWhiteSpace(preferredContextFullName)
            && TryResolveContextType(host, preferredContextFullName, out var preferred))
        {
            return [preferred];
        }

        var distinctConcreteContexts = new HashSet<Type>();

        AddDbContextTypesFromAssembly(host.PrimaryAssembly, distinctConcreteContexts);

        foreach (var assembly in host.EnumerateDiscoveryAssemblies())
        {
            if (ReferenceEquals(assembly, host.PrimaryAssembly))
            {
                continue;
            }

            AddDbContextTypesFromAssembly(assembly, distinctConcreteContexts);
        }

        return [..distinctConcreteContexts];
    }

    private static void AddDbContextTypesFromAssembly(Assembly assembly, HashSet<Type> distinctConcreteContexts)
    {
        foreach (var candidate in EnumerateDbContextCandidates(assembly))
        {
            distinctConcreteContexts.Add(candidate);
        }
    }

    private static bool TryResolveContextType(WorkspaceHost host, string contextName, out Type resolved)
    {
        resolved = null!;

        if (TryResolveContextTypeInAssembly(host.PrimaryAssembly, contextName, out resolved))
        {
            return true;
        }

        foreach (var assembly in host.EnumerateDiscoveryAssemblies())
        {
            if (ReferenceEquals(assembly, host.PrimaryAssembly))
            {
                continue;
            }

            if (TryResolveContextTypeInAssembly(assembly, contextName, out resolved))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveContextTypeInAssembly(Assembly assembly, string contextName, out Type resolved)
    {
        resolved = null!;

        if (contextName.Contains('.', StringComparison.Ordinal))
        {
            var fromGetType = assembly.GetType(contextName, false, true);

            if (fromGetType is not null && SafeIsConcreteDbContext(fromGetType))
            {
                resolved = fromGetType;
                return true;
            }
        }

        Type? uniqueMatch = null;

        foreach (var candidate in EnumerateDbContextCandidates(assembly))
        {
            if (!ContextNameMatcher.Matches(candidate, contextName))
            {
                continue;
            }

            if (uniqueMatch is not null && !ReferenceEquals(uniqueMatch, candidate))
            {
                return false;
            }

            uniqueMatch = candidate;
        }

        if (uniqueMatch is null)
        {
            return false;
        }

        resolved = uniqueMatch;

        return true;
    }

    private static IEnumerable<Type> EnumerateDbContextCandidates(Assembly assembly)
    {
        var candidates = new List<Type>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in ReflectionToolkit.EnumerateLoadableExportedTypes(assembly))
        {
            if (!SafeIsConcreteDbContext(candidate))
            {
                continue;
            }

            if (seen.Add(candidate.FullName ?? candidate.Name))
            {
                candidates.Add(candidate);
            }
        }

        foreach (var candidate in ReflectionToolkit.EnumerateLoadableTypes(assembly))
        {
            if (!SafeIsConcreteDbContext(candidate))
            {
                continue;
            }

            if (seen.Add(candidate.FullName ?? candidate.Name))
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    private static bool SafeIsConcreteDbContext(Type candidate)
    {
        try
        {
            return IsConcreteDbContext(candidate);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (FileLoadException)
        {
            return false;
        }
        catch (TypeLoadException)
        {
            return false;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    private static string BuildNoDbContextDiscoveredMessage(WorkspaceHost host, string? requestedContextFullName)
    {
        var scannedAssemblies = host.EnumerateDiscoveryAssemblies()
            .Select(static assembly => assembly.GetName().Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

        var scannedSummary = scannedAssemblies.Length == 0
            ? "No workspace assemblies were scanned."
            : "Scanned assemblies: " + string.Join(", ", scannedAssemblies)
                                     + (scannedAssemblies.Length == 12 ? ", …" : string.Empty);

        var requestedHint = string.IsNullOrWhiteSpace(requestedContextFullName)
            ? string.Empty
            : $"{Environment.NewLine}Requested context: {requestedContextFullName}";

        return
            "No concrete `Microsoft.EntityFrameworkCore.DbContext` implementations were discovered in the workspace output."
            + requestedHint
            + $"{Environment.NewLine}{scannedSummary}"
            + $"{Environment.NewLine}Output directory: {host.OutputDirectory}"
            + $"{Environment.NewLine}For class libraries, pass the persistence project with `-p` and the API with `-s` (auto-inferred when possible)."
            + $"{Environment.NewLine}Update efvibe if this persists — older builds did not load EF Core from NuGet package cache.";
    }

    private static bool IsConcreteDbContext(Type candidate)
    {
        if (!candidate.IsClass || candidate.IsAbstract)
        {
            return false;
        }

        if (string.Equals(candidate.FullName, "Microsoft.EntityFrameworkCore.DbContext", StringComparison.Ordinal))
        {
            return false;
        }

        string? namespaceName;

        try
        {
            namespaceName = candidate.Namespace;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (TypeLoadException)
        {
            return false;
        }

        if (namespaceName?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true)
        {
            return false;
        }

        var dbContextBase = ResolveDbContextBaseType(candidate.Assembly);

        if (dbContextBase is not null)
        {
            return dbContextBase.IsAssignableFrom(candidate) && !ReferenceEquals(candidate, dbContextBase);
        }

        for (var walk = candidate.BaseType; walk is not null; walk = walk.BaseType)
        {
            if (string.Equals(walk.FullName, "Microsoft.EntityFrameworkCore.DbContext", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static Type? ResolveDbContextBaseType(Assembly inspectionAnchor)
    {
        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(loaded.GetName().Name, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            {
                continue;
            }

            return loaded.GetType("Microsoft.EntityFrameworkCore.DbContext");
        }

        try
        {
            var referenced = inspectionAnchor.GetReferencedAssemblies()
                .FirstOrDefault(static name =>
                    string.Equals(name.Name, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal));

            if (referenced is null)
            {
                return null;
            }

            return AssemblyLoadContext.Default.LoadFromAssemblyName(referenced)
                .GetType("Microsoft.EntityFrameworkCore.DbContext");
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (FileLoadException)
        {
            return null;
        }
    }

    private static string FormatDesignTimeFactoryError(string? factoryFullName, string invokeError)
    {
        var couchbaseHint = CouchbaseEntityFrameworkCompatibility.TryExplainTypeLoadFailure(invokeError);

        if (couchbaseHint is null)
        {
            return $"{factoryFullName}: {invokeError}";
        }

        return $"{factoryFullName}: {invokeError}{Environment.NewLine}{couchbaseHint}";
    }

    private static bool TryCreateUsingDesignTimeFactory(
        Type dbContextConcreteType,
        WorkspaceHost host,
        out object instance,
        ref List<string>? errors)
    {
        instance = null!;

        var seenFactoryTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assembly in host.EnumerateDesignTimeDiscoveryAssemblies())
        foreach (var candidate in EnumerateFactoryCandidateTypes(assembly))
        {
            var factoryKey = candidate.AssemblyQualifiedName ?? candidate.FullName ?? candidate.Name;

            if (!seenFactoryTypes.Add(factoryKey))
            {
                continue;
            }

            if (!IsDesignTimeDbContextFactory(candidate, dbContextConcreteType))
            {
                continue;
            }

            if (!TryCreateFactoryInstance(candidate, out var factoryObject))
            {
                errors ??= [];
                errors.Add(
                    $"{candidate.FullName}: could not create factory instance (needs a public parameterless constructor).");
                continue;
            }

            if (!TryInvokeCreateDbContext(factoryObject, candidate, dbContextConcreteType, host, out var created,
                    out var invokeError))
            {
                errors ??= [];
                errors.Add(FormatDesignTimeFactoryError(candidate.FullName, invokeError));
                continue;
            }

            instance = created;
            return true;
        }

        return false;
    }

    private static IEnumerable<Type> EnumerateFactoryCandidateTypes(Assembly assembly)
    {
        var candidates = new List<Type>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in ReflectionToolkit.EnumerateLoadableExportedTypes(assembly)
                     .Concat(ReflectionToolkit.EnumerateLoadableTypes(assembly)))
        {
            var key = candidate.FullName ?? candidate.Name;

            if (seen.Add(key))
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    private static bool IsDesignTimeDbContextFactory(Type candidate, Type dbContextConcreteType)
    {
        if (!candidate.IsClass || candidate.IsAbstract)
        {
            return false;
        }

        if (candidate.GetInterfaces().Any(iface => IsDesignTimeFactoryInterface(iface, dbContextConcreteType)))
        {
            return true;
        }

        return HasCreateDbContextMethod(candidate, dbContextConcreteType);
    }

    private static bool HasCreateDbContextMethod(Type factoryType, Type dbContextConcreteType)
    {
        foreach (var method in factoryType.GetMethods(BindingFlags.Instance | BindingFlags.Public
                                                                            | BindingFlags.NonPublic))
        {
            if (!string.Equals(method.Name, "CreateDbContext", StringComparison.Ordinal))
            {
                continue;
            }

            if (!dbContextConcreteType.IsAssignableFrom(method.ReturnType))
            {
                continue;
            }

            var parameters = method.GetParameters();

            if (parameters.Length == 0)
            {
                return true;
            }

            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string[]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryCreateFactoryInstance(Type factoryType, out object factoryObject)
    {
        factoryObject = null!;

        var parameterlessCtor = factoryType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);

        if (parameterlessCtor is not null)
        {
            factoryObject = parameterlessCtor.Invoke(null)!;
            return true;
        }

        try
        {
            factoryObject = Activator.CreateInstance(factoryType)!;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryInvokeCreateDbContext(
        object factoryObject,
        Type factoryType,
        Type dbContextConcreteType,
        WorkspaceHost host,
        out object created,
        out string error)
    {
        created = null!;
        error = string.Empty;

        var createMethods = factoryType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method =>
                string.Equals(method.Name, "CreateDbContext", StringComparison.Ordinal)
                && dbContextConcreteType.IsAssignableFrom(method.ReturnType))
            .ToArray();

        var orderedMethods = createMethods
            .OrderByDescending(static method => method.GetParameters().Length)
            .ToArray();

        Exception? lastFailure = null;

        foreach (var createMethod in orderedMethods)
        {
            var parameters = createMethod.GetParameters();

            var invokeArguments = parameters switch
            {
                [] => Array.Empty<object?>(),
                [{ ParameterType: var parameterType }] when parameterType == typeof(string[])
                    => new object?[] { Array.Empty<string>() },
                _ => null
            };

            if (invokeArguments is null)
            {
                continue;
            }

            try
            {
                var workingDirectory = ResolveDesignTimeFactoryWorkingDirectory(
                    factoryType,
                    host.ProjectPath,
                    host.StartupProjectPath);

                using var currentDirectory = CurrentDirectoryScope.Enter(workingDirectory);
                var result = createMethod.Invoke(factoryObject, invokeArguments);

                if (result is null)
                {
                    error = "`CreateDbContext` returned null.";
                    return false;
                }

                created = result;
                return true;
            }
            catch (TargetInvocationException invocationFailure)
            {
                lastFailure = invocationFailure.InnerException ?? invocationFailure;
            }
            catch (Exception failure)
            {
                lastFailure = failure;
            }
        }

        if (createMethods.Length == 0)
        {
            error = "no `CreateDbContext` method found.";
            return false;
        }

        error = lastFailure?.Message ?? "CreateDbContext failed.";
        if (lastFailure is not null)
        {
            error += $"{Environment.NewLine}{lastFailure}";
        }

        return false;
    }

    internal static string ResolveDesignTimeFactoryWorkingDirectory(
        Type factoryType,
        string projectPath,
        string startupProjectPath)
    {
        var factoryAssemblyName = factoryType.Assembly.GetName().Name;
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var startupDirectory = Path.GetDirectoryName(startupProjectPath)!;

        if (ProjectAssemblyNameMatches(projectPath, factoryAssemblyName))
        {
            return projectDirectory;
        }

        if (ProjectAssemblyNameMatches(startupProjectPath, factoryAssemblyName))
        {
            return startupDirectory;
        }

        // Heuristic fallback:
        // - Visual Studio / ef tools commonly run with cwd = startup project directory
        // - but many design-time factories expect relative files (seed JSON, etc.) from the EF project directory.
        // Prefer the directory that looks more "application-root-like" based on appsettings presence.
        var projectHasAppSettings = File.Exists(Path.Combine(projectDirectory, "appsettings.json"));
        var startupHasAppSettings = File.Exists(Path.Combine(startupDirectory, "appsettings.json"));

        if (projectHasAppSettings && !startupHasAppSettings)
        {
            return projectDirectory;
        }

        return startupDirectory;
    }

    private static bool ProjectAssemblyNameMatches(string projectPath, string? assemblyName)
    {
        return !string.IsNullOrWhiteSpace(assemblyName)
               && string.Equals(
                   CsprojReader.ReadLogicalAssemblyName(projectPath),
                   assemblyName,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDesignTimeFactoryInterface(Type iface, Type dbContextConcreteType)
    {
        if (!iface.IsGenericType || iface.GenericTypeArguments.Length != 1)
        {
            return false;
        }

        if (iface.GenericTypeArguments[0] != dbContextConcreteType)
        {
            return false;
        }

        var genericDefinition = iface.IsGenericTypeDefinition
            ? iface
            : iface.GetGenericTypeDefinition();

        return genericDefinition.Name.StartsWith("IDesignTimeDbContextFactory", StringComparison.Ordinal)
               && (genericDefinition.Namespace is null
                   || genericDefinition.Namespace.Contains("EntityFrameworkCore", StringComparison.Ordinal));
    }

    private static bool TryCreateUsingParameterlessConstructor(Type dbContextConcreteType, out object instance)
    {
        var parameterlessCtor = dbContextConcreteType.GetConstructor(Type.EmptyTypes);

        if (parameterlessCtor is null)
        {
            instance = null!;

            return false;
        }

        instance = Activator.CreateInstance(dbContextConcreteType)!;

        return true;
    }

    private static void TryApplyProviderHints(
        object instance,
        WorkspaceHost host,
        MyEfVibeProvider? provider,
        ProviderDescriptor? providerDescriptor)
    {
        var providerKey = provider ?? providerDescriptor?.KnownProvider;

        if (!providerKey.HasValue)
        {
            return;
        }

        DbContextHostHints.TryApplyPostgreSqlNamingHint(
            instance,
            host.StartupProjectPath,
            providerKey.Value);
    }

    private static bool TryCreateUsingOptionsConstructor(
        Type dbContextConcreteType,
        string connectionString,
        ProviderDescriptor providerDescriptor,
        WorkspaceHost host,
        out object instance)
    {
        instance = null!;

        host.EnsureProviderDependenciesLoaded(providerDescriptor);

        var efAssembly = LoadWorkspaceAssembly(host, "Microsoft.EntityFrameworkCore");

        if (efAssembly is null)
        {
            return false;
        }

        var openBuilderType =
            efAssembly.GetType("Microsoft.EntityFrameworkCore.DbContextOptionsBuilder`1");

        if (openBuilderType is null)
        {
            return false;
        }

        var closedBuilderType =
            openBuilderType.MakeGenericType(dbContextConcreteType);

        var builderCtor = closedBuilderType.GetConstructor(Type.EmptyTypes);

        if (builderCtor is null)
        {
            return false;
        }

        var builderInstance = builderCtor.Invoke([]);

        if (builderInstance is null)
        {
            return false;
        }

        if (!EntityFrameworkProviderExtensionInvoker.TryInvoke(
                host,
                providerDescriptor,
                builderInstance,
                connectionString))
        {
            return false;
        }

        if (providerDescriptor.SupportsNamingConventionOverride
            && providerDescriptor.KnownProvider is { } namingProvider)
        {
            ProviderOptionsConfigurator.TryApplyEfCoreNamingConventions(
                host,
                builderInstance,
                namingProvider,
                connectionString);
        }

        var closedOptionsType =
            efAssembly.GetType("Microsoft.EntityFrameworkCore.DbContextOptions`1")!.MakeGenericType(
                dbContextConcreteType);

        var optionsPropertyAccessor =
            closedBuilderType.GetProperty(
                "Options",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                closedOptionsType,
                Type.EmptyTypes,
                null);

        var compiledOptionsConcreteInstance = optionsPropertyAccessor?.GetValue(builderInstance);

        if (compiledOptionsConcreteInstance is null)
        {
            return false;
        }

        var optionsInstanceType = compiledOptionsConcreteInstance.GetType();

        if (providerDescriptor.IsSqlite
            && TryCreateUsingOptionsAndDatabaseProviderAccessor(
                dbContextConcreteType,
                compiledOptionsConcreteInstance,
                host,
                out var accessorContextInstance))
        {
            instance = accessorContextInstance;
            return true;
        }

        var matchingCtor = dbContextConcreteType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(ctorCandidate =>
                ctorCandidate.GetParameters() is [{ ParameterType: var optionsParameter }]
                && (optionsParameter.IsAssignableFrom(optionsInstanceType)
                    || optionsInstanceType.IsAssignableTo(optionsParameter)));

        if (matchingCtor is null)
        {
            return false;
        }

        var createdContextInstance = matchingCtor.Invoke([compiledOptionsConcreteInstance]);

        if (createdContextInstance is null)
        {
            return false;
        }

        instance = createdContextInstance;

        if (providerDescriptor.KnownProvider is { } hintProvider)
        {
            DbContextHostHints.TryApplyPostgreSqlNamingHint(
                instance,
                host.StartupProjectPath,
                hintProvider);
        }

        return true;
    }

    private static bool TryCreateUsingOptionsAndDatabaseProviderAccessor(
        Type dbContextConcreteType,
        object compiledOptionsConcreteInstance,
        WorkspaceHost host,
        out object instance)
    {
        instance = null!;

        var accessor = TryResolveSqliteDatabaseProviderAccessor(host, dbContextConcreteType.Assembly);

        if (accessor is null)
        {
            return false;
        }

        var accessorType = accessor.GetType();
        var optionsInstanceType = compiledOptionsConcreteInstance.GetType();

        foreach (var ctor in dbContextConcreteType.GetConstructors(BindingFlags.Instance | BindingFlags.Public
                     | BindingFlags.NonPublic))
        {
            var parameters = ctor.GetParameters();

            if (parameters.Length != 2)
            {
                continue;
            }

            if (!parameters[0].ParameterType.IsAssignableFrom(optionsInstanceType)
                && !optionsInstanceType.IsAssignableTo(parameters[0].ParameterType))
            {
                continue;
            }

            if (!parameters[1].ParameterType.IsAssignableFrom(accessorType))
            {
                continue;
            }

            var created = ctor.Invoke([compiledOptionsConcreteInstance, accessor]);

            if (created is null)
            {
                continue;
            }

            instance = created;

            return true;
        }

        return false;
    }

    private static object? TryResolveSqliteDatabaseProviderAccessor(WorkspaceHost host, Assembly contextAssembly)
    {
        foreach (var assembly in EnumerateAccessorSearchAssemblies(host, contextAssembly))
        {
            foreach (var candidate in ReflectionToolkit.EnumerateLoadableExportedTypes(assembly))
            {
                if (!candidate.Name.EndsWith("SqliteDatabaseProviderAccessor", StringComparison.Ordinal)
                    || !candidate.IsClass
                    || candidate.IsAbstract)
                {
                    continue;
                }

                if (!ImplementsDatabaseProviderAccessor(candidate))
                {
                    continue;
                }

                var instanceProperty = candidate.GetProperty(
                    "Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (instanceProperty?.GetValue(null) is { } instance)
                {
                    return instance;
                }
            }
        }

        return null;
    }

    private static IEnumerable<Assembly> EnumerateAccessorSearchAssemblies(WorkspaceHost host, Assembly contextAssembly)
    {
        yield return contextAssembly;

        foreach (var loaded in host.EnumerateLoadedAssemblies())
        {
            yield return loaded;
        }
    }

    private static bool ImplementsDatabaseProviderAccessor(Type candidate)
    {
        foreach (var iface in candidate.GetInterfaces())
        {
            if (string.Equals(iface.Name, "IDatabaseProviderAccessor", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static Assembly? LoadWorkspaceAssembly(WorkspaceHost host, string assemblyName)
    {
        return host.LoadAssembly(assemblyName);
    }

    private static bool TryCreateUsingCouchbaseConfiguration(
        WorkspaceHost host,
        ref ProviderDescriptor? providerDescriptor,
        ref MyEfVibeProvider? provider,
        Type selectedDbContextType,
        out object instance)
    {
        instance = null!;

        providerDescriptor ??= EntityFrameworkProviderDiscovery.TryDiscoverFromProject(host.ProjectPath);

        if (providerDescriptor is null || !providerDescriptor.IsCouchbase)
        {
            return false;
        }

        if (!CouchbaseSettingsResolver.TryResolve(host.StartupProjectPath, out var couchbaseSettings))
        {
            return false;
        }

        provider ??= providerDescriptor.KnownProvider;

        if (!TryCreateUsingCouchbaseOptionsConstructor(
                selectedDbContextType,
                couchbaseSettings,
                providerDescriptor,
                host,
                out instance))
        {
            return false;
        }

        host.SetActiveCouchbaseSettings(couchbaseSettings);
        TryApplyProviderHints(instance, host, provider, providerDescriptor);

        return true;
    }

    private static bool TryCreateUsingCouchbaseOptionsConstructor(
        Type dbContextConcreteType,
        CouchbaseSettings couchbaseSettings,
        ProviderDescriptor providerDescriptor,
        WorkspaceHost host,
        out object instance)
    {
        instance = null!;

        host.EnsureProviderDependenciesLoaded(providerDescriptor);

        var efAssembly = LoadWorkspaceAssembly(host, "Microsoft.EntityFrameworkCore");

        if (efAssembly is null)
        {
            return false;
        }

        var openBuilderType =
            efAssembly.GetType("Microsoft.EntityFrameworkCore.DbContextOptionsBuilder`1");

        if (openBuilderType is null)
        {
            return false;
        }

        var closedBuilderType =
            openBuilderType.MakeGenericType(dbContextConcreteType);

        var builderCtor = closedBuilderType.GetConstructor(Type.EmptyTypes);

        if (builderCtor is null)
        {
            return false;
        }

        var builderInstance = builderCtor.Invoke([]);

        if (builderInstance is null)
        {
            return false;
        }

        var providerAssembly = host.LoadAssembly(providerDescriptor.ProviderAssemblyName);

        if (providerAssembly is null)
        {
            return false;
        }

        if (!ProviderConfiguratorRegistry.TryConfigureCouchbase(
                providerDescriptor,
                host,
                providerAssembly,
                builderInstance,
                couchbaseSettings))
        {
            return false;
        }

        TryApplyCamelCaseNamingConvention(host, builderInstance);

        var closedOptionsType =
            efAssembly.GetType("Microsoft.EntityFrameworkCore.DbContextOptions`1")!.MakeGenericType(
                dbContextConcreteType);

        var optionsPropertyAccessor =
            closedBuilderType.GetProperty(
                "Options",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                closedOptionsType,
                Type.EmptyTypes,
                null);

        var compiledOptionsConcreteInstance = optionsPropertyAccessor?.GetValue(builderInstance);

        if (compiledOptionsConcreteInstance is null)
        {
            return false;
        }

        var optionsInstanceType = compiledOptionsConcreteInstance.GetType();

        var matchingCtor = dbContextConcreteType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(ctorCandidate =>
                ctorCandidate.GetParameters() is [{ ParameterType: var optionsParameter }]
                && (optionsParameter.IsAssignableFrom(optionsInstanceType)
                    || optionsInstanceType.IsAssignableTo(optionsParameter)));

        if (matchingCtor is null)
        {
            return false;
        }

        var createdContextInstance = matchingCtor.Invoke([compiledOptionsConcreteInstance]);

        if (createdContextInstance is null)
        {
            return false;
        }

        instance = createdContextInstance;

        return true;
    }

    private static void TryApplyCamelCaseNamingConvention(WorkspaceHost host, object builderInstance)
    {
        var namingAssembly = host.LoadAssembly("EFCore.NamingConventions");

        if (namingAssembly is null)
        {
            return;
        }

        foreach (var exported in ReflectionToolkit.EnumerateLoadableExportedTypes(namingAssembly))
        foreach (var staticMethodCandidate in exported.GetMethods(BindingFlags.Static | BindingFlags.Public))
        {
            if (!string.Equals(
                    staticMethodCandidate.Name,
                    "UseCamelCaseNamingConvention",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = staticMethodCandidate.GetParameters();

            if (parameters.Length != 1
                || !parameters[0].ParameterType.IsAssignableFrom(builderInstance.GetType()))
            {
                continue;
            }

            staticMethodCandidate.Invoke(null, [builderInstance]);

            return;
        }
    }

    private static string NormalizeSqlServerConnectionString(string connectionString)
    {
        return SqlServerConnectionStringNormalizer.Normalize(connectionString);
    }

    private static object Finish(
        WorkspaceHost host,
        ProviderDescriptor? providerDescriptor,
        object instance)
    {
        host.SetActiveProviderDescriptor(
            providerDescriptor
            ?? EntityFrameworkProviderDiscovery.TryDiscoverFromProject(host.ProjectPath));

        if (host.ActiveProviderDescriptor?.IsCouchbase == true
            && host.ActiveCouchbaseSettings is null
            && CouchbaseSettingsResolver.TryResolve(host.StartupProjectPath, out var couchbaseSettings))
        {
            host.SetActiveCouchbaseSettings(couchbaseSettings);
        }

        CouchbaseClusterBootstrapper.TryBootstrap(instance, host);

        return instance;
    }

    private sealed class CurrentDirectoryScope : IDisposable
    {
        private readonly string _previousDirectory;

        private CurrentDirectoryScope(string workingDirectory)
        {
            _previousDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(workingDirectory);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_previousDirectory);
        }

        internal static CurrentDirectoryScope Enter(string workingDirectory)
        {
            return new CurrentDirectoryScope(workingDirectory);
        }
    }
}