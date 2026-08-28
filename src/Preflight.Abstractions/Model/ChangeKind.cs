namespace Preflight.Abstractions.Model;

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
