namespace MyEfVibe.Tests;

public sealed class LinqSqlTranslationNotesTests
{
    [Fact]
    public void IsEntityNotIncludedInModelNote_MatchesDeepScanNote()
    {
        const string note =
            "Currency is not included in the model for this DbContext"
            + " (no mapped table in the current provider configuration; SQL/EXPLAIN skipped).";

        Assert.True(LinqSqlTranslationNotes.IsEntityNotIncludedInModelNote(note));
    }

    [Fact]
    public void ShouldReplaceUnboundedMaterializeWithUnmappedEntity_WhenNoteIndicatesUnmapped()
    {
        const string note =
            "Currency is not included in the model for this DbContext"
            + " (no mapped table in the current provider configuration; SQL/EXPLAIN skipped).";

        Assert.True(LinqSqlTranslationNotes.ShouldReplaceUnboundedMaterializeWithUnmappedEntity(
            "unbounded-materialize",
            note));
    }

    [Fact]
    public void IsInvalidIncludeTranslationNote_MatchesEfCoreMessage()
    {
        const string note =
            "The expression 'a.StateProvince' is invalid inside an 'Include' operation, since it does not represent a property access: 't => t.MyProperty'.";

        Assert.True(LinqSqlTranslationNotes.IsInvalidIncludeTranslationNote(note));
    }

    [Fact]
    public void ShouldReplaceCartesianWithInvalidInclude_WhenNoteIndicatesInvalidInclude()
    {
        const string note =
            "The expression 'a.StateProvince' is invalid inside an 'Include' operation, since it does not represent a property access: 't => t.MyProperty'.";

        Assert.True(LinqSqlTranslationNotes.ShouldReplaceCartesianWithInvalidInclude("cartesian", note));
    }

    [Fact]
    public void ShouldReplaceLiteRuleWithUnmappedEntity_IncludesFirstWithoutTake()
    {
        const string note =
            "Currency is not included in the model for this DbContext"
            + " (no mapped table in the current provider configuration; SQL/EXPLAIN skipped).";

        Assert.True(LinqSqlTranslationNotes.ShouldReplaceLiteRuleWithUnmappedEntity(
            "first-without-take",
            note));
    }

    [Fact]
    public void ShouldReplaceUnboundedMaterializeWithUnmappedEntity_ReturnsFalseForOtherRules()
    {
        Assert.False(LinqSqlTranslationNotes.ShouldReplaceUnboundedMaterializeWithUnmappedEntity(
            "n-plus-one",
            "Currency is not included in the model for this DbContext"));
    }
}