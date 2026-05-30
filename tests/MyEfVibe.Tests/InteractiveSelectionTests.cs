namespace MyEfVibe.Tests;

public sealed class InteractiveSelectionTests
{
    [Theory]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    [InlineData(12, 12)]
    [InlineData(20, 12)]
    public void ResolvePromptPageSize_stays_within_spectre_console_bounds(
        int optionCount,
        int expectedPageSize)
    {
        Assert.Equal(expectedPageSize, InteractiveSelection.ResolvePromptPageSize(optionCount));
    }
}