namespace Preflight.Abstractions.Model;

/// <summary>
/// The outcome of running a rule.
/// </summary>
/// <remarks>
/// Three distinctions the tool never collapses into one bucket:
/// <see cref="Failed"/> means the workspace is wrong, <see cref="Errored"/>
/// means the rule itself is defective (an exception or a timeout), and
/// <see cref="NotApplicable"/> means the rule ran but had nothing to check — a
/// small lie as <see cref="Passed"/> would corrode trust in the report.
/// </remarks>
public enum RuleStatus
{
    Passed,
    Warning,
    Failed,
    Skipped,
    NotApplicable,
    Errored,
}
