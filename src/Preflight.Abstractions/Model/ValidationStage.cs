namespace Preflight.Abstractions.Model;

/// <summary>
/// The stage a rule runs in.
/// </summary>
/// <remarks>
/// Closed, and a plugin cannot add one. The stage determines the shape of
/// <see cref="Preflight.Abstractions.Rules.RuleContext"/>, in particular
/// whether its changed-file list is populated; a plugin adding a stage would
/// mean a context the tool has no way to populate.
/// </remarks>
public enum ValidationStage
{
    Workspace,
    PreSubmit,
    BuildReadiness,
}
