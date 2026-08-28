namespace Preflight.Core;

using Preflight.Abstractions;
using Preflight.Core.Caching;
using Preflight.Core.Policy;

/// <summary>
/// Everything one run needs.
/// </summary>
/// <remarks>
/// <see cref="RunId"/> is nullable so a caller can fix it: without that seam,
/// the console reporter's golden files could never stabilise.
/// <see cref="NoSkip"/> is the engine half of the <c>--no-skip</c> contrast
/// flag.
/// </remarks>
public sealed record RunRequest
{
    public required IReadOnlyList<IValidationRule> Rules { get; init; }

    public required EffectivePolicy Policy { get; init; }

    public required ValidationStage Stage { get; init; }

    public required BuildTarget Target { get; init; }

    public required DirectoryInfo WorkspaceRoot { get; init; }

    public required IFileSystem FileSystem { get; init; }

    public required IProcessRunner Processes { get; init; }

    /// <summary>
    /// Where cached results live, or <see langword="null"/> for no caching.
    /// </summary>
    /// <remarks>
    /// There is no <c>NoCache</c> flag beside this, deliberately.
    /// <c>--no-cache</c> is the CLI declining to hand the engine a store, which
    /// leaves the engine with one condition instead of two that have to agree —
    /// and two booleans meaning "do not cache" is how a flag ends up being
    /// honoured in one code path and ignored in another.
    /// </remarks>
    public IRuleCacheStore? Cache { get; init; }

    public IReadOnlyList<ChangedFile> ChangedFiles { get; init; } = [];

    public IReadOnlyList<string> PolicyChain { get; init; } = [];

    public string? Pipeline { get; init; }

    /// <summary>
    /// The version of the installed package the policy came from, when one did.
    /// </summary>
    /// <remarks>
    /// Carried through so that the result can say which delivery of the pipeline
    /// produced this verdict. Without it two runs of one commit against two
    /// packages are indistinguishable in every machine-readable output the tool
    /// writes. See ADR-034.
    /// </remarks>
    public string? PipelineVersion { get; init; }

    public bool FailOnWarning { get; init; }

    public bool NoSkip { get; init; }

    public Guid? RunId { get; init; }
}

/// <summary>
/// Runs the selected rules, level by level, and assembles the result.
/// </summary>
/// <remarks>
/// Rules within a level have no dependency on each other by construction, so
/// the parallelism needs no coordination beyond the level boundary — provided
/// the rules honour the concurrency contract, which the engine does not police
/// beyond the isolation of 8.2. Serialising everything to defend against a
/// badly written rule would throw away the only reason per-level parallelism
/// exists.
/// </remarks>
public sealed class RuleExecutor
{
    private readonly IRuleLoggerFactory _loggers;
    private readonly TimeProvider _timeProvider;

    public RuleExecutor(IRuleLoggerFactory loggers, TimeProvider timeProvider)
    {
        _loggers = loggers;
        _timeProvider = timeProvider;
    }

    public async Task<RunResult> ExecuteAsync(RunRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startedAt = _timeProvider.GetUtcNow();
        var startedTimestamp = _timeProvider.GetTimestamp();

        var descriptors = request.Rules.Select(rule => rule.Descriptor).ToArray();
        var graph = RuleGraph.Build(descriptors);
        var policy = request.Policy;
        var executionSet = ExecutionSet.Select(graph, descriptors, request.Stage, policy);
        var snapshots = RulePolicySnapshot.ForAll(descriptors.Select(descriptor => descriptor.Id), policy);

        var runnable = executionSet.ToExecute.ToHashSet();
        var rulesById = request.Rules.ToDictionary(rule => rule.Descriptor.Id);
        var runner = new RuleRunner(_timeProvider, CacheFor(request, policy));

        var executions = new List<RuleExecution>();
        var statuses = new Dictionary<RuleId, RuleStatus>();
        var skipped = SkipPropagation.Compute(
            graph, statuses, snapshots, executionSet.Skipped, request.NoSkip);

        var cancelled = false;

        foreach (var level in graph.Levels)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // Rules that have not started do not start. They are simply
                // absent from the record, which reports the list of what got
                // as far as executing.
                cancelled = true;
                break;
            }

            var pending = level
                .Where(runnable.Contains)
                .Where(id => !skipped.ContainsKey(id))
                .ToArray();

            var results = new List<RuleExecution>();

            await Parallel.ForEachAsync(
                pending,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxDegreeOfParallelism(policy),
                    CancellationToken = CancellationToken.None,
                },
                async (id, _) =>
                {
                    var execution = await runner.RunAsync(
                        rulesById[id], snapshots[id], ContextFor(request, policy, id), cancellationToken);

                    lock (results)
                    {
                        results.Add(execution);
                    }
                });

            executions.AddRange(results);

            foreach (var execution in results)
            {
                statuses[execution.RuleId] = execution.Status;
            }

            skipped = SkipPropagation.Compute(
                graph, statuses, snapshots, executionSet.Skipped, request.NoSkip);
        }

        cancelled |= cancellationToken.IsCancellationRequested;

        var reported = executions.Select(execution => execution.RuleId).ToHashSet();

        // A cancelled run reports only what got to execute. The alternative
        // would need a fourth SkipReason meaning "the run stopped", and skip
        // attribution has no such reason.
        if (!cancelled)
        {
            executions.AddRange(skipped.Values
                .Where(attribution => runnable.Contains(attribution.RuleId) ||
                    Disabled(executionSet, attribution.RuleId))
                .Where(attribution => !reported.Contains(attribution.RuleId))
                .Select(attribution => SkippedExecution(attribution, snapshots)));
        }

        // Partial means something never ran, not merely that the token fired.
        // Cancellation arriving after the last rule finished leaves a complete
        // record, and calling that partial would bias the history for no reason.
        var partial = cancelled &&
            (runnable.Any(id => !reported.Contains(id)) || skipped.Keys.Any(id => !reported.Contains(id)));

        var ordered = ExecutionOrdering.Sort(executions, graph);
        var verdict = RunVerdictAggregation.ApplyFailOnWarning(
            Verdict(ordered, cancelled), request.FailOnWarning);

        return new RunResult
        {
            RunId = request.RunId ?? Guid.NewGuid(),
            StartedAt = startedAt,
            Duration = _timeProvider.GetElapsedTime(startedTimestamp),
            Stage = request.Stage,
            Target = request.Target,
            Pipeline = request.Pipeline,
            PipelineVersion = request.PipelineVersion,
            PolicyChain = request.PolicyChain,
            Executions = ordered,
            Verdict = verdict,
            Partial = partial,
            FailOnWarning = request.FailOnWarning,
            NoSkip = request.NoSkip,
        };
    }

    /// <remarks>
    /// A cancelled run is <c>Errored</c> even when nothing that ran errored,
    /// and even when nothing was missed, without exception. That cannot fall
    /// out of aggregating the executions, because a run cancelled between
    /// levels leaves every completed execution perfectly healthy, so the
    /// override lives here and
    /// <see cref="RunVerdictAggregation"/> stays a pure function of what it is
    /// given.
    ///
    /// <c>Partial</c> is the finer signal and answers a different question: not
    /// "was this interrupted" but "is anything missing from the record". A run
    /// cancelled after its last rule finished is <c>Errored</c> and complete.
    /// </remarks>
    private static RunVerdict Verdict(IReadOnlyList<RuleExecution> executions, bool cancelled) =>
        cancelled ? RunVerdict.Errored : RunVerdictAggregation.Aggregate(executions);

    private static bool Disabled(ExecutionSet executionSet, RuleId ruleId) =>
        executionSet.Skipped.Any(entry => entry.RuleId == ruleId);

    private static RuleExecution SkippedExecution(
        SkipPropagation.SkipAttribution attribution,
        IReadOnlyDictionary<RuleId, RulePolicySnapshot> snapshots)
    {
        var snapshot = snapshots[attribution.RuleId];

        return new RuleExecution
        {
            RuleId = attribution.RuleId,
            Status = RuleStatus.Skipped,
            EffectiveSeverity = snapshot.EffectiveSeverity,
            Blocking = snapshot.Blocking,
            Gating = snapshot.Gating,

            // A rule that never ran took no time. Recording anything else would
            // skew the duration percentiles of the history.
            Duration = TimeSpan.Zero,
            SkipReason = attribution.Reason,
            SkippedBecauseOf = attribution.SkippedBecauseOf,
        };
    }

    private static int MaxDegreeOfParallelism(EffectivePolicy policy) =>
        (int)policy.RootValue<long>("maxDegreeOfParallelism").Value;

    /// <summary>
    /// The cache this run will use, if it uses one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>cachePath</c> is read here rather than in the CLI for the same reason
    /// <c>maxDegreeOfParallelism</c> is: it is a resolved policy value, and the
    /// engine is what holds the resolved policy. The CLI decides
    /// <em>whether</em> to cache; policy decides <em>where</em>.
    /// </para>
    /// <para>
    /// A <c>--no-skip</c> run gets none, in either direction. That flag is a
    /// contrast run whose whole purpose is to see what every rule says right
    /// now, and serving it a stored answer defeats the purpose; more
    /// importantly, it executes rules whose gating dependency failed, so the
    /// results it produces are exactly the ones the graph exists to prevent
    /// anybody from relying on. Letting those fill the cache would leak them
    /// into ordinary runs.
    /// </para>
    /// </remarks>
    private static RuleCache? CacheFor(RunRequest request, EffectivePolicy policy) =>
        request.Cache is null || request.NoSkip
            ? null
            : new RuleCache(
                request.Cache,
                CachePaths.DirectoryFor(request.WorkspaceRoot, CacheSettings.From(policy).Path),
                policy);

    private RuleContext ContextFor(RunRequest request, EffectivePolicy policy, RuleId ruleId) => new()
    {
        WorkspaceRoot = request.WorkspaceRoot,
        Stage = request.Stage,
        Target = request.Target,
        ChangedFiles = request.ChangedFiles,
        Policy = policy.ReaderFor(ruleId),
        Logger = _loggers.ForRule(ruleId),
        FileSystem = request.FileSystem,
        Processes = request.Processes,
    };
}
