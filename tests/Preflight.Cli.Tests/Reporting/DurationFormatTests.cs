namespace Preflight.Cli.Tests.Reporting;

using Preflight.Cli.Reporting;

/// <summary>
/// The one place a duration becomes text, and both scales it has to hold.
/// </summary>
/// <remarks>
/// Extracted from the console reporter when the report needed the same
/// numbers at a different scale. The sixteen console golden files are the
/// regression harness for the extraction itself: they came back byte-identical
/// without being re-accepted.
/// </remarks>
public sealed class DurationFormatTests
{
    /// <remarks>
    /// One decimal, always, including the trailing zero. The console report's report
    /// aligns a duration column, and <c>1s</c> next to <c>0.4s</c> does not.
    /// </remarks>
    [Theory]
    [InlineData(0, "0.0s")]
    [InlineData(0.04, "0.0s")]
    [InlineData(0.4, "0.4s")]
    [InlineData(1, "1.0s")]
    [InlineData(14.9, "14.9s")]
    [InlineData(59.94, "59.9s")]
    public void Seconds_IsOneDecimalOfSeconds(double seconds, string expected) =>
        DurationFormat.Seconds(TimeSpan.FromSeconds(seconds)).ShouldBe(expected);

    /// <summary>
    /// The scale of the report, which holds a rule and a build in one column.
    /// </summary>
    /// <remarks>
    /// The two literals the report draws are here: <c>38m02s</c> for a build
    /// of 2282 seconds and <c>9h30m</c> for a ceiling of 34230. The zero padding
    /// is not cosmetic — without it the column stops aligning and two reports
    /// stop being diffable, which the determinism guarantee spends real design on.
    /// </remarks>
    [Theory]
    [InlineData(0, "0.0s")]
    [InlineData(18.4, "18.4s")]
    [InlineData(59.9, "59.9s")]
    [InlineData(60, "1m00s")]
    [InlineData(62, "1m02s")]
    [InlineData(2282, "38m02s")]
    [InlineData(3599, "59m59s")]
    [InlineData(3600, "1h00m")]
    [InlineData(34230, "9h30m")]
    [InlineData(90000, "25h00m")]
    public void Scaled_PicksTheUnitAndPadsTheSmallerOne(double seconds, string expected) =>
        DurationFormat.Scaled(TimeSpan.FromSeconds(seconds)).ShouldBe(expected);
}
