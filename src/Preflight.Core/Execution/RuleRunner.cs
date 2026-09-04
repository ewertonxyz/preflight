namespace Preflight.Core.Execution;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core.Caching;

/// <summary>
/// Runs one rule, and survives whatever it does.
/// </summary>
/// <remarks>
/// Every rule runs inside a try/catch: an exception becomes
/// <see cref="RuleStatus.Errored"/> with its stack trace, and the run carries
/// on. A third party's defective rule does not take the run down — but it does
/// not go unnoticed either, because <c>Errored</c> outranks every other status
/// in the verdict.
///
/// A timeout also produces <c>Errored</c>, never <c>Failed</c>, for the same
/// reason: a rule that did not finish never said the workspace was wrong, it
/// said that it itself was, or that the machine was slower than the policy
/// assumed.
/// </remarks>
public sealed class RuleRunner
{
    private readonly TimeProvider _timeProvider;
    private readonly RuleCache? _cache;

    /// <param name="timeProvider">The clock durations are measured against.</param>
    /// <param name="cache">
    /// The incremental cache, or <see langword="null"/> when there is none.
    /// </param>
    /// <remarks>
    /// Null rather than a no-op instance, and defaulted so that the majority of
    /// this class's tests — which are about isolation and have nothing to say
    /// about caching — read the same as they did before the cache existed. The
    /// engine never decides whether to cache; it is handed a cache or it is
    /// not, and <c>--no-cache</c> is the CLI declining to hand one over.
    /// </remarks>
    public RuleRunner(TimeProvider timeProvider, RuleCache? cache = null)
    {
        _timeProvider = timeProvider;
        _cache = cache;
    }

    public async Task<RuleExecution> RunAsync(
        IValidationRule rule,
        RulePolicySnapshot policy,
        RuleContext context,
        CancellationToken runToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(context);

        if (runToken.IsCancellationRequested)
        {
            return Cancelled(policy, TimeSpan.Zero);
        }

        var startedAt = _timeProvider.GetTimestamp();

        using var timeout = new CancellationTokenSource(policy.Timeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(runToken, timeout.Token);

        Task<RuleOutcome> running;

        // The cache and the key travel together, because a key without the
        // cache that minted it is not a thing this method can act on. Kept as
        // two independent locals, every use of the key had to assert that the
        // cache was there as well, and an assertion is only as true as the last
        // person to move the code above it.
        (RuleCache Cache, string Key)? caching = null;

        try
        {
            // The fingerprint is the rule's own code, so it runs under the same
            // deadline and inside the same isolation as the rule itself. A
            // fingerprint that hangs is a rule that hangs.
            if (_cache is { } cache)
            {
                if (await cache.KeyForAsync(rule, context, linked.Token) is { } key)
                {
                    if (await cache.TryReadAsync(policy.RuleId, key, context, linked.Token) is { } cached)
                    {
                        // The duration recorded is the lookup, not the run that
                        // originally produced this. It is drawn as 0.0s for
                        // that reason: the history would otherwise report a
                        // duration that did not happen in this run.
                        return Complete(cached, policy, Elapsed(startedAt)) with { FromCache = true };
                    }

                    caching = (cache, key);
                }
            }

            // Invoking is its own step, not folded into the await: a rule whose
            // ExecuteAsync is not async throws before ever handing back a task,
            // and a try that wrapped only the await would let that one escape
            // and take the run down with it.
            running = rule.ExecuteAsync(context, linked.Token);
        }
        catch (OperationCanceledException) when (runToken.IsCancellationRequested)
        {
            return Cancelled(policy, Elapsed(startedAt));
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return TimedOut(policy, Elapsed(startedAt));
        }
        catch (Exception exception)
        {
            return Errored(policy, Elapsed(startedAt), exception.ToString());
        }

        // The task is held rather than only awaited: when a rule ignores its
        // token there is nothing to cancel, and .NET offers no way to abort a
        // task, so the runner stops waiting and keeps the handle to observe the
        // fault later.
        try
        {
            var outcome = await running.WaitAsync(linked.Token);
            var execution = Complete(outcome, policy, Elapsed(startedAt));

            if (caching is { } store)
            {
                // Deliberately not the linked token. Cancellation landing between
                // the rule finishing and the result being stored would turn a
                // completed rule into an Errored one over a write nobody was
                // waiting for.
                await store.Cache.WriteAsync(policy.RuleId, store.Key, outcome, context, CancellationToken.None);
            }

            return execution;
        }
        catch (OperationCanceledException) when (runToken.IsCancellationRequested)
        {
            ObserveIfAbandoned(running, context, policy);

            return Cancelled(policy, Elapsed(startedAt));
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            ObserveIfAbandoned(running, context, policy);

            return TimedOut(policy, Elapsed(startedAt));
        }
        catch (Exception exception)
        {
            // Deliberately broad, and deliberately not rethrown. A rule runs
            // isolated so that no rule can end the run for everybody else, and
            // narrowing this catch would decide which third-party defects are
            // allowed to take the build down.
            return Errored(policy, Elapsed(startedAt), exception.ToString());
        }
    }

    /// <remarks>
    /// A rule that never honours its token cannot be stopped. Waiting for it
    /// would make the timeout advisory and let one bad plugin hang a build
    /// forever, so the runner reports and moves on — but the abandoned task
    /// still has to have its fault observed, or it resurfaces later as an
    /// unobserved exception that tears the process down during something
    /// unrelated.
    /// </remarks>
    private static void ObserveIfAbandoned(Task<RuleOutcome> running, RuleContext context, RulePolicySnapshot policy)
    {
        // Attached unconditionally: a continuation on an already-completed task
        // runs at once, and if that task failed while the run was being
        // cancelled its fault still needs observing. Guarding on IsCompleted
        // would add a branch whose false side no test can reach on purpose.
        _ = running.ContinueWith(
            task =>
            {
                if (task.Exception is { } fault)
                {
                    context.Logger.Warn(
                        $"Rule '{policy.RuleId}' was abandoned after it stopped responding to cancellation, " +
                        $"and later failed with: {fault.GetBaseException().Message}");
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private TimeSpan Elapsed(long startedAt) => _timeProvider.GetElapsedTime(startedAt);

    /// <remarks>
    /// The contract reserves <see cref="RuleStatus.Skipped"/> and
    /// <see cref="RuleStatus.Errored"/> for the engine and gives a rule no
    /// factory for either — but <c>RuleOutcome.Status</c> is a public init
    /// property, so a rule can still claim one. Passing that through would put
    /// a skip in the report with no cause attached — and a skip whose cause
    /// nobody can name is the one the reader cannot act on, so it is reported
    /// as the contract violation it is.
    /// </remarks>
    private static RuleExecution Complete(RuleOutcome outcome, RulePolicySnapshot policy, TimeSpan duration)
    {
        if (outcome is null)
        {
            return Errored(policy, duration, "The rule returned no outcome.");
        }

        if (outcome.Status is RuleStatus.Skipped or RuleStatus.Errored)
        {
            return Errored(
                policy,
                duration,
                $"The rule declared status '{outcome.Status}', which only the engine may produce. " +
                "Skipped and Errored are produced by the engine, never by a rule.");
        }

        return Base(policy, duration) with
        {
            Status = outcome.Status,
            Findings = outcome.Findings,
        };
    }

    private static RuleExecution TimedOut(RulePolicySnapshot policy, TimeSpan duration) =>
        Errored(policy, duration, $"The rule timed out after {policy.Timeout.TotalSeconds:0.###} seconds.");

    private static RuleExecution Cancelled(RulePolicySnapshot policy, TimeSpan duration) =>
        Errored(policy, duration, "The run was cancelled before this rule could finish.");

    private static RuleExecution Errored(RulePolicySnapshot policy, TimeSpan duration, string detail) =>
        Base(policy, duration) with
        {
            Status = RuleStatus.Errored,
            ErrorDetail = detail,
        };

    private static RuleExecution Base(RulePolicySnapshot policy, TimeSpan duration) => new()
    {
        RuleId = policy.RuleId,
        Status = RuleStatus.Passed,
        EffectiveSeverity = policy.EffectiveSeverity,
        Blocking = policy.Blocking,
        Gating = policy.Gating,
        Duration = duration,
    };
}
