namespace Preflight.Core.Tests.History;

using Preflight.Core.History;

/// <summary>
/// The <c>historyMode</c> literals of the history format.
/// </summary>
/// <remarks>
/// The unknown case is tested here, at the one place it is reachable by calling
/// a method. Policy validation already closes the key to two literals, so a
/// <c>switch</c> further downstream would carry an arm no input reaches — which
/// is either a permanent hole in the branch count or a fabricated test written
/// to close it.
/// </remarks>
public sealed class HistoryModeParserTests
{
    [Theory]
    [InlineData("shared", HistoryMode.Shared)]
    [InlineData("per-process", HistoryMode.PerProcess)]
    public void Parse_ForEachDocumentedValue_ReturnsTheMode(string value, HistoryMode expected) =>
        HistoryModeParser.Parse(value).ShouldBe(expected);

    /// <remarks>
    /// Ordinal and case-sensitive, like every other policy literal in the
    /// project. <c>"Shared"</c> is not a spelling of <c>shared</c>; it is a typo
    /// the validator names.
    /// </remarks>
    [Theory]
    [InlineData("Shared")]
    [InlineData("PER-PROCESS")]
    [InlineData("perprocess")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_ForAValueThePolicyWouldHaveRefused_ReturnsNull(string? value) =>
        HistoryModeParser.Parse(value).ShouldBeNull();

    [Fact]
    public void AcceptedValues_AreTheTwoSection101Documents() =>
        HistoryModeParser.AcceptedValues.ShouldBe(["shared", "per-process"]);
}
