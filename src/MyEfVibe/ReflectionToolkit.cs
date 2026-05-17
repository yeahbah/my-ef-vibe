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
            return Array.Empty<Type>();
        }
        catch (FileLoadException)
        {
            return Array.Empty<Type>();
        }
        catch (TypeLoadException)
        {
            return Array.Empty<Type>();
        }
    }
}
