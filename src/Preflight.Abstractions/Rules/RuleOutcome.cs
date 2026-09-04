namespace Preflight.Abstractions.Rules;

using Preflight.Abstractions.Model;

/// <summary>
/// The result of running a rule once.
/// </summary>
/// <remarks>
/// <para>
/// There are deliberately no <c>Skipped()</c> nor <c>Errored()</c> factories:
/// those two statuses are produced by the tool — gating propagation and
/// exception or timeout isolation, respectively — not by a rule declaring
/// itself either one.
/// </para>
/// <para>
/// <see cref="Status"/> is a public <c>init</c> property, so
/// <c>new RuleOutcome { Status = RuleStatus.Skipped }</c> still compiles. The
/// type does not stop it; the tool does. A rule returning either status is
/// recorded as <see cref="RuleStatus.Errored"/> against a message naming the
/// status it claimed, because passing it through would put a skip in the
/// report with no cause attached to it. Keeping the check in the tool rather
/// than in a private setter here is what lets a rule author learn four
/// factories and nothing else.
/// </para>
/// <para>
/// The factories copy the findings they are handed. <see cref="Findings"/> is
/// declared as a read-only list and would otherwise be a view onto an array
/// the caller still holds, so a rule refilling one buffer per iteration would
/// silently rewrite outcomes it had already returned. The object initializer
/// does not copy, for the same reason it does not check <see cref="Status"/>:
/// the factories are the supported way for a rule to produce one of these.
/// </para>
/// </remarks>
public sealed record RuleOutcome
{
    public required RuleStatus Status { get; init; }

    public IReadOnlyList<Finding> Findings { get; init; } = [];

    public static RuleOutcome Passed() =>
        new() { Status = RuleStatus.Passed };

    public static RuleOutcome NotApplicable() =>
        new() { Status = RuleStatus.NotApplicable };

    public static RuleOutcome Warned(params Finding[] findings) =>
        new() { Status = RuleStatus.Warning, Findings = [.. findings] };

    public static RuleOutcome Failed(params Finding[] findings) =>
        new() { Status = RuleStatus.Failed, Findings = [.. findings] };
}
