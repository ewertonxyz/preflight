namespace Preflight.Cli.Tests.Parsing;

using Preflight.Cli.Parsing;

/// <summary>
/// The <c>--since</c> grammar.
/// </summary>
public sealed class SinceDurationTests
{
    [Theory]
    [InlineData("30d", 30, 'd', 30 * 24 * 60)]
    [InlineData("1d", 1, 'd', 24 * 60)]
    [InlineData("12h", 12, 'h', 12 * 60)]
    [InlineData("90m", 90, 'm', 90)]
    [InlineData("365d", 365, 'd', 365 * 24 * 60)]
    public void Parse_ForEachAcceptedWindow_ReturnsIt(
        string value,
        long amount,
        char unit,
        int expectedMinutes)
    {
        var window = SinceDuration.Parse(value).ShouldNotBeNull();

        window.Amount.ShouldBe(amount);
        window.Unit.ShouldBe(unit);
        window.Value.ShouldBe(TimeSpan.FromMinutes(expectedMinutes));
    }

    /// <summary>
    /// Everything else is refused, and the refusal is exit 2.
    /// </summary>
    /// <remarks>
    /// Zero and negative windows are refused rather than clamped: a report over
    /// an empty window prints zeroes, which reads exactly like a month in which
    /// nothing was ever validated. The overflow row matters for a different
    /// reason — <c>TimeSpan</c> throws on it, and an exception escaping a parser
    /// turns a clean exit 2 into an unhandled crash.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("d")]
    [InlineData("30")]
    [InlineData("30x")]
    [InlineData("30 d")]
    [InlineData("-1d")]
    [InlineData("+1d")]
    [InlineData("0d")]
    [InlineData("0h")]
    [InlineData("1.5d")]
    [InlineData("1,5d")]
    [InlineData("30D")]
    [InlineData("999999999999999999999d")]
    [InlineData("99999999999999d")]
    public void Parse_ForAnythingElse_ReturnsNull(string? value) =>
        SinceDuration.Parse(value).ShouldBeNull();

    /// <remarks>
    /// Rendered from what the user typed, because <c>720h</c> and <c>30d</c> are
    /// the same window and only one of them is what somebody asked for.
    /// </remarks>
    [Theory]
    [InlineData("30d", "30 days")]
    [InlineData("1d", "1 day")]
    [InlineData("1h", "1 hour")]
    [InlineData("48h", "48 hours")]
    [InlineData("1m", "1 minute")]
    [InlineData("90m", "90 minutes")]
    public void Describe_SaysTheWindowInWords(string value, string expected) =>
        SinceDuration.Parse(value).ShouldNotBeNull().Describe().ShouldBe(expected);

    [Fact]
    public void AcceptedUnits_AreTheThreeAdr022Names() =>
        SinceDuration.AcceptedUnits.ShouldBe(["d", "h", "m"]);
}
