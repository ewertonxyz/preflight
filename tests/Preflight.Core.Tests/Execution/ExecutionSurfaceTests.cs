namespace Preflight.Core.Tests.Execution;

using Preflight.Core;
using Preflight.Core.Execution;

/// <summary>
/// Pins the execution contracts of execution against silent drift.
/// </summary>
/// <remarks>
/// <see cref="RunVerdict"/> and <see cref="SkipReason"/> live in
/// <c>Preflight.Core</c> rather than in <c>Preflight.Abstractions</c>: no rule
/// ever produces either, only the tool's own records use them, and the plugin
/// version contract versions only the Abstractions surface.
/// <c>EnumSurfaceTests</c> pins the Abstractions enums as an exact set, so
/// putting them there would have broken that test — which was the signal that
/// it was the wrong home.
/// </remarks>
public sealed class ExecutionSurfaceTests
{
    [Fact]
    public void RunVerdict_DefinesExactlyTheFourVerdictsOfSection8()
    {
        Enum.GetNames<RunVerdict>().ShouldBe(
            ["Passed", "PassedWithWarnings", "Blocked", "Errored"], ignoreOrder: true);
    }

    [Fact]
    public void SkipReason_DefinesExactlyTheThreeReasonsOfSection73()
    {
        Enum.GetNames<SkipReason>().ShouldBe(
            ["DependencyFailed", "DependencyErrored", "DependencyDisabled"], ignoreOrder: true);
    }

    /// <remarks>
    /// The optional half of a <see cref="RuleExecution"/>. <c>FromCache</c> in
    /// particular must be present and false rather than absent: the cache key
    /// requires a cached result to be marked as such, and a field that only
    /// appears once caching exists would leave every historical record
    /// ambiguous.
    /// </remarks>
    [Fact]
    public void RuleExecution_LeavesItsOptionalMembersEmptyByDefault()
    {
        var execution = new RuleExecution
        {
            RuleId = new Abstractions.Rules.RuleId("core.a.alpha"),
            Status = Abstractions.Model.RuleStatus.Passed,
            EffectiveSeverity = Abstractions.Model.Severity.Error,
            Blocking = true,
            Gating = true,
            Duration = TimeSpan.Zero,
        };

        execution.Findings.ShouldBeEmpty();
        execution.SkippedBecauseOf.ShouldBeEmpty();
        execution.SkipReason.ShouldBeNull();
        execution.ErrorDetail.ShouldBeNull();
        execution.FromCache.ShouldBeFalse();
    }
}
