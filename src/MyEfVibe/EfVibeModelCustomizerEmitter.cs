using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace MyEfVibe;

/// <summary>
///     Emits an <c>IModelCustomizer</c> implementation against the workspace EF Core assembly so
///     provider-specific relational naming applies after <c>OnModelCreating</c>.
/// </summary>
internal static class EfVibeModelCustomizerEmitter
{
    private static readonly ConcurrentDictionary<(Assembly EfAssembly, string ApplierTypeName, long RegistrationId), Type?>
        Cache = new();

    internal static Type? TryGetOrCreate(WorkspaceHost host, MethodInfo afterBaseMethod, long registrationId = 0)
    {
        var efAssembly = host.LoadAssembly("Microsoft.EntityFrameworkCore");

        if (efAssembly is null)
        {
            return null;
        }

        var applierTypeName = afterBaseMethod.DeclaringType?.FullName
                              ?? afterBaseMethod.DeclaringType?.Name
                              ?? afterBaseMethod.Name;

        return Cache.GetOrAdd(
            (efAssembly, applierTypeName, registrationId),
            key => EmitCustomizer(key.EfAssembly, afterBaseMethod, key.RegistrationId));
    }

    private static Type? EmitCustomizer(Assembly efAssembly, MethodInfo afterBaseMethod, long registrationId)
    {
        var modelCustomizerType = efAssembly.GetType(
            "Microsoft.EntityFrameworkCore.Infrastructure.ModelCustomizer",
            false);

        var depsType = efAssembly.GetType(
            "Microsoft.EntityFrameworkCore.Infrastructure.ModelCustomizerDependencies",
            false);

        var modelBuilderType = efAssembly.GetType("Microsoft.EntityFrameworkCore.ModelBuilder", false);
        var dbContextType = efAssembly.GetType("Microsoft.EntityFrameworkCore.DbContext", false);
        var baseCustomize = modelCustomizerType?.GetMethod(
            "Customize",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [modelBuilderType!, dbContextType!],
            null);

        var baseCtor = modelCustomizerType?.GetConstructor([depsType!]);

        if (modelCustomizerType is null
            || depsType is null
            || modelBuilderType is null
            || dbContextType is null
            || baseCustomize is null
            || baseCtor is null)
        {
            return null;
        }

        var afterBaseCall = ResolveAfterBaseMethod(afterBaseMethod, modelBuilderType, dbContextType, registrationId);

        if (afterBaseCall is null)
        {
            return null;
        }

        var assemblyName = new AssemblyName(
            $"MyEfVibe.ModelCustomizer_{afterBaseMethod.DeclaringType!.Name}_{registrationId}_{efAssembly.GetName().Version}");
        var asmBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var moduleBuilder = asmBuilder.DefineDynamicModule("MainModule");
        var typeBuilder = moduleBuilder.DefineType(
            $"MyEfVibe.{afterBaseMethod.DeclaringType.Name}Customizer_{registrationId}",
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

        var overrideBuilder = typeBuilder.DefineMethod(
            "Customize",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.ReuseSlot |
            MethodAttributes.HideBySig,
            baseCustomize.ReturnType,
            [modelBuilderType, dbContextType]);

        var il = overrideBuilder.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, baseCustomize);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);

        if (registrationId > 0)
        {
            il.Emit(OpCodes.Ldc_I8, registrationId);
        }

        il.Emit(OpCodes.Call, afterBaseCall);
        il.Emit(OpCodes.Ret);

        return typeBuilder.CreateType();
    }

    private static MethodInfo? ResolveAfterBaseMethod(
        MethodInfo afterBaseMethod,
        Type modelBuilderType,
        Type dbContextType,
        long registrationId)
    {
        if (registrationId <= 0)
        {
            return afterBaseMethod;
        }

        return afterBaseMethod.DeclaringType?.GetMethod(
            afterBaseMethod.Name,
            BindingFlags.Public | BindingFlags.Static,
            null,
            [modelBuilderType, dbContextType, typeof(long)],
            null);
    }
}
