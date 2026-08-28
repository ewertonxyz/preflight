namespace Preflight.Abstractions;

/// <summary>
/// The stage a rule runs in.
/// </summary>
/// <remarks>
/// Closed by design, and a plugin cannot add one. The stage determines the
/// shape of <see cref="RuleContext"/>, in particular whether
/// <see cref="RuleContext.ChangedFiles"/> is populated; a plugin adding a stage
/// would mean a context the engine has no way to populate.
/// </remarks>
public enum ValidationStage
{
    Workspace,
    PreSubmit,
    BuildReadiness,
}

/// <summary>
/// The severity a rule runs at. Owned by policy, never by the rule itself.
/// </summary>
public enum Severity
{
    Information,
    Warning,
    Error,
}

/// <summary>
/// The outcome of running a rule.
/// </summary>
/// <remarks>
/// Three distinctions the engine never collapses into one bucket:
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

/// <summary>
/// What happened to a file between the diff base and the workspace.
/// </summary>
public enum ChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed,
}
