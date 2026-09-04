namespace Preflight.Abstractions.Rules;

using Preflight.Abstractions.Model;

/// <summary>
/// A single validation rule.
/// </summary>
/// <remarks>
/// A rule does not decide whether it blocks, its final severity, whether it
/// gates its dependents, or anything about other rules — it only reports
/// passed, warned, failed or not-applicable, with evidence.
///
/// A rule needs a public parameterless constructor; the tool discovers types
/// by reflection and instantiates with <c>Activator.CreateInstance</c>.
/// Services reach the rule through <see cref="RuleContext"/>, not through the
/// constructor, so that <c>Preflight.Abstractions</c> never depends on a
/// dependency-injection container.
/// </remarks>
public interface IValidationRule
{
    /// <summary>What this rule is, what it depends on, and its defaults.</summary>
    RuleDescriptor Descriptor { get; }

    /// <summary>Runs the rule once and reports what it found.</summary>
    /// <remarks>
    /// <para>
    /// Four obligations, and each of them has a visible consequence when it is
    /// not met, because the tool would rather report a broken rule than
    /// quietly absorb one.
    /// </para>
    /// <para>
    /// <b>Report a wrong workspace as <see cref="RuleStatus.Failed"/>, never by
    /// throwing.</b> The two are different facts and the report keeps them
    /// apart: <c>Failed</c> says the workspace is wrong, and
    /// <see cref="RuleStatus.Errored"/> says this rule is. An exception escaping
    /// here becomes <c>Errored</c> carrying the stack trace, which tells a
    /// content author that the tool is broken when what was broken was their
    /// commit.
    /// </para>
    /// <para>
    /// <b>Do not claim <see cref="RuleStatus.Skipped"/> or
    /// <see cref="RuleStatus.Errored"/>.</b> The tool produces both — the
    /// first from gating propagation, the second from an exception or a
    /// timeout — and a rule that returns either is itself recorded as
    /// <c>Errored</c>, naming the status it claimed. <see cref="RuleOutcome"/>
    /// offers a factory for each status a rule may produce and none for these
    /// two.
    /// </para>
    /// <para>
    /// <b>Check <paramref name="cancellationToken"/> in any loop over
    /// workspace-sized input.</b> A pre-submit rule can be handed tens of
    /// thousands of changed files, and one that never looks at the token cannot
    /// be stopped — not by the user's interrupt and not by its own timeout,
    /// which is enforced by cancelling this token rather than by killing a
    /// thread. A loop over a handful of policy patterns does not need the
    /// check; a loop over the workspace does.
    /// </para>
    /// <para>
    /// <b>Return an outcome.</b> A <see langword="null"/> result becomes
    /// <c>Errored</c> saying the rule returned no outcome; there is no status
    /// that means "nothing to report". A rule with nothing to look at reports
    /// <see cref="RuleStatus.NotApplicable"/>, which is a different claim from
    /// <see cref="RuleStatus.Passed"/> — a tick over a check that had nothing
    /// to look at claims more than is known, and that distinction is why the
    /// status exists.
    /// </para>
    /// </remarks>
    /// <param name="context">The services, the workspace and the changed files for this run.</param>
    /// <param name="cancellationToken">Cancelled by the user's interrupt and by this rule's timeout.</param>
    Task<RuleOutcome> ExecuteAsync(
        RuleContext context,
        CancellationToken cancellationToken);
}
