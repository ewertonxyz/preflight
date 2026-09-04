namespace Preflight.Core.Tests.History;

using Preflight.Abstractions.Model;
using Preflight.Core.Execution;
using Preflight.Core.History;

/// <summary>
/// Builds history entries for the report tests.
/// </summary>
/// <remarks>
/// The defaults are the uninteresting case — a passing build-readiness run of
/// one second with nothing in it — so that each test states only the fact it is
/// about. <c>HistoryEvent.Run</c> has eight required members, and without this
/// every test would open with eight lines in which the one that matters is
/// invisible.
/// </remarks>
public static class HistoryEntries
{
    public static readonly DateTimeOffset Now = new(2026, 8, 26, 14, 30, 0, TimeSpan.Zero);

    public static HistoryEntry Run(
        TimeSpan? ago = null,
        RunVerdict verdict = RunVerdict.Passed,
        double seconds = 1,
        ValidationStage stage = ValidationStage.BuildReadiness,
        bool partial = false,
        bool failOnWarning = false,
        bool noSkip = false,
        IReadOnlyList<HistoryExecution>? executions = null) =>
        new HistoryEntry.Parsed(new HistoryEvent.Run
        {
            StartedAt = Now - (ago ?? TimeSpan.FromHours(1)),
            Duration = TimeSpan.FromSeconds(seconds),
            Stage = stage,
            Verdict = verdict,
            Partial = partial,
            FailOnWarning = failOnWarning,
            NoSkip = noSkip,
            ExecutedCount = executions?.Count ?? 0,
            Executions = executions ?? [],
        });

    public static HistoryEntry External(string label, double seconds, TimeSpan? ago = null) =>
        new HistoryEntry.Parsed(new HistoryEvent.External
        {
            StartedAt = Now - (ago ?? TimeSpan.FromHours(1)),
            Duration = TimeSpan.FromSeconds(seconds),
            Label = label,
            ExitCode = 0,
        });

    public static HistoryExecution Execution(string ruleId, RuleStatus status, double seconds) =>
        new(ruleId, status, TimeSpan.FromSeconds(seconds));

    /// <summary>
    /// <paramref name="count"/> copies of <paramref name="entry"/>, for reaching
    /// a percentile's minimum sample.
    /// </summary>
    public static IEnumerable<HistoryEntry> Repeat(HistoryEntry entry, int count) =>
        Enumerable.Repeat(entry, count);
}
