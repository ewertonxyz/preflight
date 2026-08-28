namespace Preflight.Cli.Reporting;

using Preflight.Abstractions.Model;

/// <summary>
/// The two SARIF enumerations, derived from one execution.
/// </summary>
/// <remarks>
/// Public because the exhaustiveness is the part that rots: a seventh
/// <see cref="RuleStatus"/> or a fourth <see cref="Severity"/> has to fail a
/// test rather than fall through a discard. Public is what a test exercises,
/// and that is the whole reason this is not private to the reporter.
/// </remarks>
public static class SarifMapping
{
    /// <summary>The SARIF <c>kind</c> of a result nothing is wrong with.</summary>
    public const string Pass = "pass";

    /// <summary>The SARIF <c>kind</c> of a result that reports a problem.</summary>
    public const string Fail = "fail";

    /// <summary>The SARIF <c>kind</c> of a rule that did not evaluate.</summary>
    public const string NotApplicable = "notApplicable";

    /// <summary>The SARIF <c>level</c> a result carries when it is not a failure.</summary>
    public const string None = "none";

    /// <summary>
    /// The SARIF <c>kind</c> of an execution that ended in
    /// <paramref name="status"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Warning</c> and <c>Failed</c> are both <c>fail</c>: both are
    /// statements about the workspace, and what separates them is the level,
    /// which is severity and belongs to policy. <c>Passed</c> is emitted rather
    /// than dropped, because a consumer that never sees a passing rule loses
    /// the proof that the rule ran at all.
    /// </para>
    /// <para>
    /// <c>Errored</c> has no kind, and reaching this method with one throws
    /// <see cref="ArgumentOutOfRangeException"/>. A rule defect never accuses
    /// the workspace, so it is not a result at all — it goes to
    /// <c>invocations[].toolExecutionNotifications</c>, and a reporter that
    /// forgot to filter it should stop loudly rather than write a kind that
    /// makes the tool's own bug look like a verdict on somebody's commit.
    /// Returning a null, or <see cref="NotApplicable"/>, are both quieter and
    /// both worse: the first writes <c>"kind": null</c> that nobody notices,
    /// and the second is the accusation this decision exists to prevent.
    /// </para>
    /// </remarks>
    public static string KindOf(RuleStatus status) => status switch
    {
        RuleStatus.Passed => Pass,
        RuleStatus.Warning => Fail,
        RuleStatus.Failed => Fail,
        RuleStatus.Skipped => NotApplicable,
        RuleStatus.NotApplicable => NotApplicable,
        _ => throw new ArgumentOutOfRangeException(
            nameof(status),
            status,
            "Errored has no SARIF kind: a rule defect is an invocation notification, not a result."),
    };

    /// <summary>
    /// The SARIF <c>level</c> of an execution.
    /// </summary>
    /// <remarks>
    /// The SARIF standard requires <c>level</c> to be <c>none</c> whenever
    /// <c>kind</c> is not <c>fail</c>, so the status decides before the
    /// severity is consulted. That ordering is load-bearing rather than
    /// pedantic: rules default to <see cref="Severity.Error"/>, so reading the
    /// severity first would give every passing rule <c>"level": "error"</c> and
    /// mark a green run red on every code review screen — in a document that
    /// still parses and still validates.
    ///
    /// The rest follows from severity belonging to the rule and to policy,
    /// never to the finding. <c>Errored</c> throws here for the same reason it
    /// throws in <see cref="KindOf"/>, which this delegates to.
    /// </remarks>
    public static string LevelOf(RuleStatus status, Severity severity) =>
        KindOf(status) == Fail ? LevelOf(severity) : None;

    private static string LevelOf(Severity severity) => severity switch
    {
        Severity.Information => "note",
        Severity.Warning => "warning",
        Severity.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unmapped severity."),
    };
}
