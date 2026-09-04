namespace Preflight.Core.Tests.Execution;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core;
using Preflight.Core.Execution;

/// <summary>
/// Fixes the four-row table of verdict aggregation, in the order it
/// states: the first condition satisfied wins.
/// </summary>
/// <remarks>
/// <para>
/// <c>Errored</c> ranks first deliberately: a defect in the tool is never
/// reported as a problem with the workspace. If a rule blew up, the run does not
/// know what it would have said, and calling that <c>Blocked</c> accuses the
/// developer of a mistake that may not exist.
/// </para>
/// <para>
/// The aggregation reads <c>Blocking</c> from the <em>recorded execution</em>,
/// never from the policy. That is what keeps a report honest after the policy
/// changes, and it is why the signature takes executions and nothing else.
/// </para>
/// </remarks>
public sealed class RunVerdictAggregationTests
{
    [Fact]
    public void Aggregate_WithNoExecutions_IsPassed()
    {
        RunVerdictAggregation.Aggregate([]).ShouldBe(RunVerdict.Passed);
    }

    [Fact]
    public void Aggregate_WithEveryRulePassed_IsPassed()
    {
        RunVerdictAggregation.Aggregate([Execution(RuleStatus.Passed), Execution(RuleStatus.Passed)])
            .ShouldBe(RunVerdict.Passed);
    }

    /// <remarks>
    /// Verdict aggregation: neither ever decides the verdict on its own. A skip always
    /// has a root cause that was already counted, and a not-applicable means
    /// there was nothing to say.
    /// </remarks>
    [Fact]
    public void Aggregate_WithOnlySkippedAndNotApplicable_IsPassed()
    {
        RunVerdictAggregation.Aggregate([Execution(RuleStatus.Skipped), Execution(RuleStatus.NotApplicable)])
            .ShouldBe(RunVerdict.Passed);
    }

    [Fact]
    public void Aggregate_WithAnErroredRuleAlongsideABlockingFailure_IsErrored()
    {
        RunVerdictAggregation.Aggregate([
            Execution(RuleStatus.Failed, blocking: true),
            Execution(RuleStatus.Errored),
        ]).ShouldBe(RunVerdict.Errored);
    }

    [Fact]
    public void Aggregate_WithABlockingFailureAlongsideAWarning_IsBlocked()
    {
        RunVerdictAggregation.Aggregate([
            Execution(RuleStatus.Warning),
            Execution(RuleStatus.Failed, blocking: true),
        ]).ShouldBe(RunVerdict.Blocked);
    }

    /// <remarks>
    /// The right-hand half of row 3, and the arm most easily left uncovered.
    /// </remarks>
    [Fact]
    public void Aggregate_WithANonBlockingFailure_IsPassedWithWarnings()
    {
        RunVerdictAggregation.Aggregate([Execution(RuleStatus.Failed, blocking: false)])
            .ShouldBe(RunVerdict.PassedWithWarnings);
    }

    [Fact]
    public void Aggregate_WithAWarning_IsPassedWithWarnings()
    {
        RunVerdictAggregation.Aggregate([Execution(RuleStatus.Warning)])
            .ShouldBe(RunVerdict.PassedWithWarnings);
    }

    /// <remarks>
    /// <c>blocking</c> is defined over <c>Failed</c> alone. A warning from a
    /// blocking rule is still a warning — the opposite reading fuses severity
    /// and verdict, which are separate concerns.
    /// </remarks>
    [Fact]
    public void Aggregate_WithAWarningFromABlockingRule_IsStillPassedWithWarnings()
    {
        RunVerdictAggregation.Aggregate([Execution(RuleStatus.Warning, blocking: true)])
            .ShouldBe(RunVerdict.PassedWithWarnings);
    }

    /// <remarks>
    /// The same separation from the other direction: severity says how to communicate,
    /// <c>blocking</c> says whether the run fails. Deriving one from the other
    /// re-fuses two axes the design keeps apart.
    /// </remarks>
    [Fact]
    public void Aggregate_WithABlockingFailureAtInformationSeverity_IsStillBlocked()
    {
        RunVerdictAggregation.Aggregate([
            Execution(RuleStatus.Failed, blocking: true, severity: Severity.Information),
        ]).ShouldBe(RunVerdict.Blocked);
    }

    [Fact]
    public void ApplyFailOnWarning_WithPassedWithWarnings_BecomesBlocked()
    {
        RunVerdictAggregation.ApplyFailOnWarning(RunVerdict.PassedWithWarnings, failOnWarning: true)
            .ShouldBe(RunVerdict.Blocked);
    }

    [Theory]
    [InlineData(RunVerdict.Passed)]
    [InlineData(RunVerdict.Blocked)]
    [InlineData(RunVerdict.Errored)]
    public void ApplyFailOnWarning_WithAnyOtherVerdict_LeavesItUnchanged(RunVerdict verdict)
    {
        RunVerdictAggregation.ApplyFailOnWarning(verdict, failOnWarning: true).ShouldBe(verdict);
    }

    [Theory]
    [InlineData(RunVerdict.Passed)]
    [InlineData(RunVerdict.PassedWithWarnings)]
    [InlineData(RunVerdict.Blocked)]
    [InlineData(RunVerdict.Errored)]
    public void ApplyFailOnWarning_WhenTheFlagIsFalse_LeavesEveryVerdictUnchanged(RunVerdict verdict)
    {
        RunVerdictAggregation.ApplyFailOnWarning(verdict, failOnWarning: false).ShouldBe(verdict);
    }

    private static RuleExecution Execution(
        RuleStatus status, bool blocking = true, Severity severity = Severity.Error) => new()
        {
            RuleId = new RuleId($"core.a.rule-{(int)status}"),
            Status = status,
            EffectiveSeverity = severity,
            Blocking = blocking,
            Gating = true,
            Duration = TimeSpan.Zero,
        };
}
