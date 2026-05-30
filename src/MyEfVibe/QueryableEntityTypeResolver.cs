namespace MyEfVibe;

internal static class QueryableEntityTypeResolver
{
    internal static bool TryExtractConcreteEntityTypeName(
        string code,
        Type dbContextType,
        out string entityTypeName)
    {
        if (SetEntityTypeExtractor.TryExtractConcreteEntityTypeName(code, out entityTypeName))
        {
            return true;
        }

        return DbSetPropertyEntityExtractor.TryExtractConcreteEntityTypeName(code, dbContextType, out entityTypeName);
    }
}