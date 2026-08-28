namespace Preflight.Abstractions;

/// <summary>
/// Everything a rule receives from the engine to do its work.
/// </summary>
/// <remarks>
/// Exactly four services, and deliberately no <see cref="IChangeSource"/> among
/// them: it populates <see cref="ChangedFiles"/> for the engine, it is never
/// delivered to the rule itself.
/// </remarks>
public sealed class RuleContext
{
    public required DirectoryInfo WorkspaceRoot { get; init; }

    public required ValidationStage Stage { get; init; }

    public required BuildTarget Target { get; init; }

    public required IReadOnlyList<ChangedFile> ChangedFiles { get; init; }

    public required IPolicyReader Policy { get; init; }

    public required IRuleLogger Logger { get; init; }

    public required IFileSystem FileSystem { get; init; }

    public required IProcessRunner Processes { get; init; }
}

public sealed record BuildTarget(string Platform, string Configuration);

/// <summary>
/// A single file touched between the diff base and the workspace.
/// </summary>
/// <remarks>
/// Populated from the diff in <see cref="ValidationStage.PreSubmit"/> and empty
/// otherwise. <see cref="PreviousRelativePath"/> is meant to be set only when
/// <see cref="Kind"/> is <see cref="ChangeKind.Renamed"/>, but that is a
/// convention for whoever produces the list — the type itself does not enforce
/// it. See IDEAS.md.
/// </remarks>
public sealed record ChangedFile(
    string RelativePath,
    ChangeKind Kind,
    string? PreviousRelativePath = null);
