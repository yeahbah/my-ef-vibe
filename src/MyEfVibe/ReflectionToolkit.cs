using System.Reflection;

namespace MyEfVibe;

internal static class ReflectionToolkit
{
    internal static IEnumerable<Type> EnumerateLoadableExportedTypes(Assembly inspectionAssembly)
    {
        try
        {
            return inspectionAssembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException reflectedTypesLoaderFailure)
        {
            return reflectedTypesLoaderFailure.Types.Where(static candidate => candidate is not null).Cast<Type>();
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (FileLoadException)
        {
            return [];
        }
        catch (TypeLoadException)
        {
            return [];
        }
    }

    internal static IEnumerable<Type> EnumerateLoadableTypes(Assembly inspectionAssembly)
    {
        try
        {
            return inspectionAssembly.GetTypes();
        }
        catch (ReflectionTypeLoadException loaderFailure)
        {
            return loaderFailure.Types.Where(static candidate => candidate is not null).Cast<Type>();
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (FileLoadException)
        {
            return [];
        }
        catch (TypeLoadException)
        {
            return [];
        }
    }
}