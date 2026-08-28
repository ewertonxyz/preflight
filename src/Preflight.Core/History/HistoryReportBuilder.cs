namespace Preflight.Core.History;

using Preflight.Abstractions;

/// <summary>
/// Folds the history into the numbers <c>preflight report</c> prints.
/// </summary>
/// <remarks>
/// <para>
/// Consumes the reader's sequence as it arrives and keeps only the
/// accumulators, so the memory this needs is a function of how many distinct
/// rules and labels the history mentions rather than of how long the history
/// is, which is what keeps the report affordable as the history grows.
/// </para>
/// <para>
/// Every exclusion here is deliberate, and each one exists because the obvious
/// implementation produces a number that is wrong in a way nobody would notice:
/// a cancelled run drags the median down, a skipped execution at zero
/// milliseconds inverts "slowest rules", and a warning promoted by a flag
/// inflates the count of what the tool caught.
/// </para>
/// </remarks>
public static class HistoryReportBuilder
{
    /// <summary>
    /// The label whose median the upper bound is built from.
    /// </summary>
    public const string BuildLabel = "build";

    /// <summary>How many rules each of the two rankings shows.</summary>
    public const int TopRuleCount = 5;

    /// <summary>
    /// Reads <paramref name="entries"/> to the end and summarises what fell
    /// inside the window.
    /// </summary>
    /// <param name="entries">The history, as the reader yields it.</param>
    /// <param name="now">The instant the window is measured back from.</param>
    /// <param name="window">How far back to look. The lower edge is inclusive.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task<HistoryReport> BuildAsync(
        IAsyncEnumerable<HistoryEntry> entries,
        DateTimeOffset now,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var state = new Accumulator(now - window);

        await foreach (var entry in entries.WithCancellation(cancellationToken))
        {
            state.Add(entry);
        }

        return state.Build(window);
    }

    /// <remarks>
    /// A private mutable fold rather than LINQ over a materialised list. The
    /// sequence is walked once by construction, which is what keeps the cost
    /// linear, and it cannot be walked twice by accident later.
    /// </remarks>
    private sealed class Accumulator
    {
        private readonly DateTimeOffset _floor;
        private readonly List<TimeSpan> _preflightDurations = [];
        private readonly Dictionary<string, List<TimeSpan>> _measured = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<TimeSpan>> _ruleDurations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _ruleFailures = new(StringComparer.Ordinal);
        private readonly Dictionary<ValidationStage, int> _blocking = [];

        private int _runs;
        private int _passed;
        private int _passedWithWarnings;
        private int _blocked;
        private int _errored;
        private int _promoted;
        private int _contrast;
        private int _partial;
        private int _unreadable;
        private int _ignored;

        public Accumulator(DateTimeOffset floor)
        {
            _floor = floor;
        }

        /// <remarks>
        /// The discard arm carries <c>Parsed</c> rather than a fourth
        /// <c>case</c> followed by an unreachable default. <c>HistoryEntry</c>
        /// is a closed hierarchy — abstract, private constructor, three nested
        /// sealed records — so a line that is neither unreadable nor ignored is
        /// a parsed one, and spelling that out as its own case would compile a
        /// fall-through no input can reach: a permanent hole in the branch
        /// count, or a fabricated test written to close it. The same argument
        /// <c>EffectivePolicy.Flatten</c> makes about <c>PolicyNode</c>.
        /// </remarks>
        public void Add(HistoryEntry entry)
        {
            switch (entry)
            {
                case HistoryEntry.Unreadable:
                    // Counted whatever the window is: a line nobody can read has
                    // no instant to compare against, and silently dropping it is
                    // what this refuses to do.
                    _unreadable++;

                    return;

                case HistoryEntry.Ignored:
                    _ignored++;

                    return;

                default:
                    var parsed = (HistoryEntry.Parsed)entry;

                    if (parsed.Value.StartedAt >= _floor)
                    {
                        Add(parsed.Value);
                    }

                    return;
            }
        }

        public HistoryReport Build(TimeSpan window) => new()
        {
            Window = window,
            RunCount = _runs,
            PassedCount = _passed,
            PassedWithWarningsCount = _passedWithWarnings,
            BlockedCount = _blocked,
            ErroredCount = _errored,
            BlockingVerdicts = [.. _blocking
                .OrderBy(entry => entry.Key)
                .Select(entry => new StageBlockCount(entry.Key, entry.Value))],
            PromotedBlockCount = _promoted,
            ContrastRunCount = _contrast,
            PartialRunCount = _partial,
            UnreadableLineCount = _unreadable,
            IgnoredLineCount = _ignored,
            PreflightDuration = DurationSummary.Of(_preflightDurations),
            Measured = [.. _measured
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new MeasuredSeries(entry.Key, DurationSummary.Of(entry.Value)))],
            SlowestRules = [.. Slowest().Take(TopRuleCount)],
            SlowestRulesNotShown = Math.Max(0, Slowest().Count() - TopRuleCount),
            MostFrequentFailures = [.. Failures().Take(TopRuleCount)],
            MostFrequentFailuresNotShown = Math.Max(0, Failures().Count() - TopRuleCount),
            UpperBoundNotSpent = UpperBound(),
        };

        private void Add(HistoryEvent value)
        {
            if (value is HistoryEvent.External external)
            {
                Series(_measured, external.Label).Add(external.Duration);

                return;
            }

            var run = (HistoryEvent.Run)value;

            _runs++;

            switch (run.Verdict)
            {
                case RunVerdict.Passed:
                    _passed++;
                    break;
                case RunVerdict.PassedWithWarnings:
                    _passedWithWarnings++;
                    break;
                case RunVerdict.Errored:
                    _errored++;
                    break;
                default:
                    _blocked++;
                    Blocked(run);
                    break;
            }

            if (run.NoSkip)
            {
                _contrast++;
            }

            if (run.Partial)
            {
                // A cancelled run is recorded so it is not invisible.
                // Nothing there says its partial duration is a duration, and a
                // run interrupted at three seconds enters the median as a
                // three-second run.
                _partial++;
            }
            else
            {
                _preflightDurations.Add(run.Duration);
            }

            foreach (var execution in run.Executions)
            {
                Add(execution, run.Partial);
            }
        }

        private void Add(HistoryExecution execution, bool partial)
        {
            if (execution.Status is RuleStatus.Failed or RuleStatus.Errored)
            {
                _ruleFailures[execution.RuleId] = _ruleFailures.GetValueOrDefault(execution.RuleId) + 1;
            }

            // Three exclusions, for three reasons. A skip costs nothing and
            // takes no time, and a zero next to a real measurement does not
            // average with it - it replaces the ranking with one about how often
            // a rule was skipped. A cancelled run was cut off mid-flight: the
            // record says which rules ran but not which one the cancellation
            // interrupted, so none of its durations can be trusted as a
            // duration. And a cached result is a lookup, not a run - counting it
            // would make the slowest rule look fast in exact proportion to how
            // well it caches, which is the measurement that says whether the
            // cache was worth building at all.
            if (partial ||
                execution.FromCache ||
                execution.Status is RuleStatus.Skipped or RuleStatus.NotApplicable)
            {
                return;
            }

            Series(_ruleDurations, execution.RuleId).Add(execution.Duration);
        }

        private void Blocked(HistoryEvent.Run run)
        {
            // Without a Failed execution, the only thing that could have blocked
            // a run under --fail-on-warning is the promotion itself. A truncated
            // record still carries every execution's status, so this stays
            // answerable for the records the 64 KB cap replaced.
            if (run.FailOnWarning &&
                !run.Executions.Any(execution => execution.Status is RuleStatus.Failed or RuleStatus.Errored))
            {
                _promoted++;

                return;
            }

            _blocking[run.Stage] = _blocking.GetValueOrDefault(run.Stage) + 1;
        }

        private static List<TimeSpan> Series(Dictionary<string, List<TimeSpan>> into, string key)
        {
            if (!into.TryGetValue(key, out var series))
            {
                series = [];
                into[key] = series;
            }

            return series;
        }

        private IEnumerable<RuleDuration> Slowest() => _ruleDurations
            .Select(entry => new { entry.Key, Median = PercentileCalculator.P50(entry.Value) })
            .Where(entry => entry.Median is not null)
            .OrderByDescending(entry => entry.Median!.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new RuleDuration(entry.Key, entry.Median!.Value));

        private IEnumerable<RuleFailureCount> Failures() => _ruleFailures
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new RuleFailureCount(entry.Key, entry.Value));

        /// <remarks>
        /// Build readiness only. A block at pre-submit saves a review round,
        /// not a build, and multiplying it by a build duration would be the
        /// kind of number the report spends three lines of caveat refusing to
        /// print.
        /// </remarks>
        private TimeSpan? UpperBound()
        {
            if (!_measured.TryGetValue(BuildLabel, out var builds) ||
                PercentileCalculator.P50(builds) is not { } median)
            {
                return null;
            }

            return median * _blocking.GetValueOrDefault(ValidationStage.BuildReadiness);
        }
    }
}
