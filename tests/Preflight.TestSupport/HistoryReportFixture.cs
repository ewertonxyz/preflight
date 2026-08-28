namespace Preflight.TestSupport;

using Preflight.Abstractions;
using Preflight.Core.History;

/// <summary>
/// Builds the reports the renderer tests print.
/// </summary>
/// <remarks>
/// <para>
/// The documented example is the <c>report</c> screen, number for number, so
/// the golden file is a comparison against the design document rather than
/// against whatever the renderer happened to produce first.
/// </para>
/// <para>
/// It moved here from <c>Cli.Tests</c> when the reporters gave <c>Core.Tests</c> a
/// second reason to build one: <c>HistoryReportDocument</c> projects the same
/// report the console renders, and two fixtures producing "the documented
/// example" would be two examples. Same reason as
/// <see cref="RunResultFixture"/>, same phase's worth of drift avoided.
/// </para>
/// </remarks>
public static class HistoryReportFixture
{
    /// <summary>
    /// The documented screen: 142 runs, a measured build, and a ceiling.
    /// </summary>
    public static HistoryReport DocumentedExample() => new()
    {
        Window = TimeSpan.FromDays(30),
        RunCount = 142,
        PassedCount = 118,
        PassedWithWarningsCount = 9,
        BlockedCount = 15,
        ErroredCount = 0,
        BlockingVerdicts = [new StageBlockCount(ValidationStage.BuildReadiness, 15)],
        PromotedBlockCount = 0,
        ContrastRunCount = 0,
        PartialRunCount = 0,
        UnreadableLineCount = 0,
        IgnoredLineCount = 0,
        PreflightDuration = new DurationSummary(
            142,
            TimeSpan.FromSeconds(18.4),
            TimeSpan.FromSeconds(31.2)),
        Measured =
        [
            new MeasuredSeries("build", new DurationSummary(27, TimeSpan.FromSeconds(2282), null)),
        ],
        SlowestRules =
        [
            new RuleDuration("core.build.compile-probe", TimeSpan.FromSeconds(14.9)),
            new RuleDuration("core.workspace.dependencies", TimeSpan.FromSeconds(2.1)),
            new RuleDuration("core.build.configuration", TimeSpan.FromSeconds(0.8)),
        ],
        SlowestRulesNotShown = 0,
        MostFrequentFailures =
        [
            new RuleFailureCount("core.workspace.toolchain", 9),
            new RuleFailureCount("core.presubmit.large-file", 4),
            new RuleFailureCount("core.build.configuration", 2),
        ],
        MostFrequentFailuresNotShown = 0,
        UpperBoundNotSpent = TimeSpan.FromSeconds(2282 * 15),
    };

    /// <summary>A history nobody has written to yet.</summary>
    public static HistoryReport Empty() => new()
    {
        Window = TimeSpan.FromDays(30),
        RunCount = 0,
        PassedCount = 0,
        PassedWithWarningsCount = 0,
        BlockedCount = 0,
        ErroredCount = 0,
        BlockingVerdicts = [],
        PromotedBlockCount = 0,
        ContrastRunCount = 0,
        PartialRunCount = 0,
        UnreadableLineCount = 0,
        IgnoredLineCount = 0,
        PreflightDuration = DurationSummary.Empty,
        Measured = [],
        SlowestRules = [],
        SlowestRulesNotShown = 0,
        MostFrequentFailures = [],
        MostFrequentFailuresNotShown = 0,
        UpperBoundNotSpent = null,
    };
}
