namespace Preflight.Core;

using Preflight.Abstractions;

/// <summary>
/// Turns a set of executions into the run's verdict.
/// </summary>
/// <remarks>
/// <para>
/// Evaluated in order, first match winning. <c>Errored</c> ranks first on
/// purpose: a defect in the tool is never reported as a defect in the
/// workspace. If a rule blew up, the run does not know what it would have said,
/// and calling that <c>Blocked</c> would accuse the developer of a mistake that
/// may not exist.
/// </para>
/// <para>
/// <c>Skipped</c> and <c>NotApplicable</c> never decide anything by themselves.
/// A skip always has a root cause that has already been counted; a
/// not-applicable means there was nothing to say.
/// </para>
/// <para>
/// The signature takes executions and nothing else, deliberately. Reading
/// <c>blocking</c> from the recorded execution rather than from live policy is
/// what keeps a verdict reproducible from history after the policy has changed.
/// </para>
/// </remarks>
public static class RunVerdictAggregation
{
    public static RunVerdict Aggregate(IReadOnlyList<RuleExecution> executions)
    {
        ArgumentNullException.ThrowIfNull(executions);

        if (executions.Any(execution => execution.Status is RuleStatus.Errored))
        {
            return RunVerdict.Errored;
        }

        if (executions.Any(execution => execution.Status is RuleStatus.Failed && execution.Blocking))
        {
            return RunVerdict.Blocked;
        }

        if (executions.Any(execution =>
            execution.Status is RuleStatus.Warning ||
            (execution.Status is RuleStatus.Failed && !execution.Blocking)))
        {
            return RunVerdict.PassedWithWarnings;
        }

        return RunVerdict.Passed;
    }

    /// <remarks>
    /// Applied after aggregation, as a last transformation: it turns
    /// <see cref="RunVerdict.PassedWithWarnings"/> into
    /// <see cref="RunVerdict.Blocked"/> and touches nothing else. It does not
    /// turn <c>Passed</c> into anything, and it does not soften <c>Errored</c>.
    /// </remarks>
    public static RunVerdict ApplyFailOnWarning(RunVerdict verdict, bool failOnWarning) =>
        failOnWarning && verdict is RunVerdict.PassedWithWarnings ? RunVerdict.Blocked : verdict;
}
