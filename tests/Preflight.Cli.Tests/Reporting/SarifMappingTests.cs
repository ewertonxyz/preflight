namespace Preflight.Cli.Tests.Reporting;

using Preflight.Abstractions.Model;
using Preflight.Cli.Reporting;

/// <summary>
/// Fixes the two SARIF enumerations against the decisions behind them.
/// </summary>
/// <remarks>
/// Separate from the reporter's own tests because this is the part of the phase
/// that is easiest to get wrong and cheapest to prove in isolation: a level that
/// disagreed with a kind would still produce a document that parses, validates
/// against the schema, and says the wrong thing on somebody's review screen.
/// </remarks>
public sealed class SarifMappingTests
{
    /// <remarks>
    /// Many-to-one on purpose. <c>Warning</c> and <c>Failed</c> are both
    /// <c>fail</c> because both are findings about the workspace; what separates
    /// them is the level, which is severity and comes from policy.
    /// </remarks>
    [Theory]
    [InlineData(RuleStatus.Passed, "pass")]
    [InlineData(RuleStatus.Warning, "fail")]
    [InlineData(RuleStatus.Failed, "fail")]
    [InlineData(RuleStatus.Skipped, "notApplicable")]
    [InlineData(RuleStatus.NotApplicable, "notApplicable")]
    public void KindOf_ForEveryStatusThatProducesAResult_MatchesTheDecidedMapping(
        RuleStatus status,
        string expected) =>
        SarifMapping.KindOf(status).ShouldBe(expected);

    /// <summary>
    /// <c>Errored</c> has no kind, and asking for one stops the run.
    /// </summary>
    /// <remarks>
    /// The glossary's second false friend at the level of the mapping. Verdict aggregation
    /// puts <c>Errored</c> first in aggregation so a defect in the rule is
    /// never reported as a problem with the workspace, and this keeps it out
    /// of <c>results</c> entirely. An <c>Errored</c> arriving here is a reporter
    /// that forgot to filter it, and the two quiet alternatives are both worse:
    /// a null writes <c>"kind": null</c> that nobody notices, and
    /// <c>notApplicable</c> passes the tool's own bug off as a statement about
    /// somebody's commit.
    /// </remarks>
    [Fact]
    public void KindOf_ForErrored_ThrowsRatherThanProducingAKind() =>
        Should.Throw<ArgumentOutOfRangeException>(() => SarifMapping.KindOf(RuleStatus.Errored));

    /// <summary>
    /// A kind that is not <c>fail</c> is level <c>none</c>, whatever the rule's
    /// severity says.
    /// </summary>
    /// <remarks>
    /// The trap at the centre of this mapping, and it is silent. Rules carry
    /// <c>Severity.Error</c> by default (the rule descriptor), so a reporter that read
    /// the severity before the status would give every passing rule
    /// <c>"level": "error"</c> — marking a success as a failure on every code
    /// review screen, in a document that still parses and still validates. The
    /// SARIF standard requires <c>none</c> whenever the kind is not <c>fail</c>.
    /// </remarks>
    [Theory]
    [InlineData(RuleStatus.Passed, Severity.Information)]
    [InlineData(RuleStatus.Passed, Severity.Warning)]
    [InlineData(RuleStatus.Passed, Severity.Error)]
    [InlineData(RuleStatus.Skipped, Severity.Information)]
    [InlineData(RuleStatus.Skipped, Severity.Warning)]
    [InlineData(RuleStatus.Skipped, Severity.Error)]
    [InlineData(RuleStatus.NotApplicable, Severity.Information)]
    [InlineData(RuleStatus.NotApplicable, Severity.Warning)]
    [InlineData(RuleStatus.NotApplicable, Severity.Error)]
    public void LevelOf_WhenTheKindIsNotFail_IsNoneWhateverTheSeverity(
        RuleStatus status,
        Severity severity) =>
        SarifMapping.LevelOf(status, severity).ShouldBe("none");

    /// <remarks>
    /// severity belongs to the rule and to policy, never to the
    /// finding. The level is where that decision surfaces in SARIF.
    /// </remarks>
    [Theory]
    [InlineData(RuleStatus.Warning, Severity.Information, "note")]
    [InlineData(RuleStatus.Warning, Severity.Warning, "warning")]
    [InlineData(RuleStatus.Warning, Severity.Error, "error")]
    [InlineData(RuleStatus.Failed, Severity.Information, "note")]
    [InlineData(RuleStatus.Failed, Severity.Warning, "warning")]
    [InlineData(RuleStatus.Failed, Severity.Error, "error")]
    public void LevelOf_WhenTheKindIsFail_DerivesFromTheEffectiveSeverity(
        RuleStatus status,
        Severity severity,
        string expected) =>
        SarifMapping.LevelOf(status, severity).ShouldBe(expected);

    /// <remarks>
    /// The level delegates to the kind, so the two stop in the same place.
    /// </remarks>
    [Fact]
    public void LevelOf_ForErrored_ThrowsRatherThanProducingALevel() =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => SarifMapping.LevelOf(RuleStatus.Errored, Severity.Error));

    /// <summary>
    /// A fourth severity is a failing test, not a silent level.
    /// </summary>
    /// <remarks>
    /// Not in the phase's manifest, and added for the reason
    /// <c>ExitCodeTests.ForVerdict_WithAValueOutsideTheEnum_Throws</c> exists: a
    /// switch over a closed enum needs a final arm to compile, and an arm no
    /// test reaches is an arm nobody knows the behaviour of. The cast is how
    /// this repository already covers that arm rather than excluding it.
    /// </remarks>
    [Fact]
    public void LevelOf_WithASeverityOutsideTheEnum_Throws() =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => SarifMapping.LevelOf(RuleStatus.Failed, (Severity)99));
}
