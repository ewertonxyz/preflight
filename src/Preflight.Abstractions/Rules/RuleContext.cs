namespace Preflight.Abstractions.Rules;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Services;

/// <summary>
/// Everything a rule receives from the tool to do its work.
/// </summary>
/// <remarks>
/// Exactly four services, and deliberately no <see cref="IChangeSource"/> among
/// them: it populates <see cref="ChangedFiles"/> for the tool, it is never
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
