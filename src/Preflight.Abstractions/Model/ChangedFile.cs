namespace Preflight.Abstractions.Model;

/// <summary>
/// A single file touched between the diff base and the workspace.
/// </summary>
/// <remarks>
/// Populated from the diff in <see cref="ValidationStage.PreSubmit"/> and empty
/// otherwise. <see cref="PreviousRelativePath"/> is meant to be set only when
/// <see cref="Kind"/> is <see cref="ChangeKind.Renamed"/>, and the record does
/// not enforce it — any combination of the two constructs without error, where
/// <see cref="Preflight.Abstractions.Rules.RuleId"/> validates its own
/// invariant in a constructor. The asymmetry holds while the git change source
/// is the only producer: validation here would guard against a mistake nobody
/// has made. It earns its place once there is a second producer to check
/// against.
/// </remarks>
public sealed record ChangedFile(
    string RelativePath,
    ChangeKind Kind,
    string? PreviousRelativePath = null);
