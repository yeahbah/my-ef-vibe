namespace MyEfVibe;

internal static class LinqSqlTranslationNotes
{
    internal const string EntityNotIncludedInModelPhrase = "not included in the model for this DbContext";

    private static readonly string[] LiteRulesReplacedByUnmappedEntity =
    [
        "unbounded-materialize",
        "first-without-take"
    ];

    internal static bool IsEntityNotIncludedInModelNote(string? note)
    {
        return !string.IsNullOrWhiteSpace(note)
               && note.Contains(EntityNotIncludedInModelPhrase, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldReplaceUnboundedMaterializeWithUnmappedEntity(
        string ruleId,
        string? sqlTranslationNote)
    {
        return ShouldReplaceLiteRuleWithUnmappedEntity(ruleId, sqlTranslationNote);
    }

    internal static bool ShouldReplaceLiteRuleWithUnmappedEntity(
        string ruleId,
        string? sqlTranslationNote)
    {
        return IsEntityNotIncludedInModelNote(sqlTranslationNote)
               && LiteRulesReplacedByUnmappedEntity.Contains(ruleId, StringComparer.Ordinal);
    }

    internal static bool IsInvalidIncludeTranslationNote(string? note)
    {
        return !string.IsNullOrWhiteSpace(note)
               && note.Contains("invalid inside an 'Include' operation", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldReplaceCartesianWithInvalidInclude(
        string ruleId,
        string? sqlTranslationNote)
    {
        return string.Equals(ruleId, "cartesian", StringComparison.Ordinal)
               && IsInvalidIncludeTranslationNote(sqlTranslationNote);
    }
}