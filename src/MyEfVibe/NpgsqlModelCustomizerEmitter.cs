using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace MyEfVibe;

/// <summary>
/// Emits an <c>IModelCustomizer</c> implementation against the workspace EF Core assembly so
/// lowercase PostgreSQL identifiers apply after <c>OnModelCreating</c> (when sample <c>UsesPostgreSql()</c> is false).
/// </summary>
internal static class NpgsqlModelCustomizerEmitter
{
    private static readonly ConcurrentDictionary<Assembly, Type?> Cache = new();

    internal static Type? TryGetOrCreate(WorkspaceHost host)
    {
        var efAssembly = host.LoadAssembly("Microsoft.EntityFrameworkCore");

        if (efAssembly is null)
            return null;

        return Cache.GetOrAdd(efAssembly, EmitCustomizer);
    }

    private static Type? EmitCustomizer(Assembly efAssembly)
    {
        var modelCustomizerType = efAssembly.GetType(
            "Microsoft.EntityFrameworkCore.Infrastructure.ModelCustomizer",
            throwOnError: false);

        var depsType = efAssembly.GetType(
            "Microsoft.EntityFrameworkCore.Infrastructure.ModelCustomizerDependencies",
            throwOnError: false);

        var modelBuilderType = efAssembly.GetType("Microsoft.EntityFrameworkCore.ModelBuilder", throwOnError: false);
        var dbContextType = efAssembly.GetType("Microsoft.EntityFrameworkCore.DbContext", throwOnError: false);
        var baseCustomize = modelCustomizerType?.GetMethod(
            "Customize",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [modelBuilderType!, dbContextType!],
            modifiers: null);

        var baseCtor = modelCustomizerType?.GetConstructor([depsType!]);

        if (modelCustomizerType is null
            || depsType is null
            || modelBuilderType is null
            || dbContextType is null
            || baseCustomize is null
            || baseCtor is null)
            return null;

        var assemblyName = new AssemblyName($"MyEfVibe.NpgsqlModelCustomizer_{efAssembly.GetName().Version}");
        var asmBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var moduleBuilder = asmBuilder.DefineDynamicModule("MainModule");
        var typeBuilder = moduleBuilder.DefineType(
            "MyEfVibe.EfvibeNpgsqlModelCustomizer",
            TypeAttributes.Public | TypeAttributes.Class,
            modelCustomizerType);

        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            [depsType]);

        var ctorIl = ctorBuilder.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Call, baseCtor);
        ctorIl.Emit(OpCodes.Ret);

        var afterBase = typeof(PostgreSqlRelationalNamingApplier).GetMethod(
            nameof(PostgreSqlRelationalNamingApplier.CustomizeAfterBase),
            BindingFlags.Static | BindingFlags.Public)!;

        var overrideBuilder = typeBuilder.DefineMethod(
            "Customize",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.ReuseSlot | MethodAttributes.HideBySig,
            baseCustomize.ReturnType,
            [modelBuilderType, dbContextType]);

        var il = overrideBuilder.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, baseCustomize);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, afterBase);
        il.Emit(OpCodes.Ret);

        return typeBuilder.CreateType();
    }
}
