namespace Preflight.Core.Execution;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Core.Caching;
using Preflight.Core.Policy;

/// <summary>
/// Everything one run needs.
/// </summary>
/// <remarks>
/// <see cref="RunId"/> is nullable so a caller can fix it. Left to generate its
/// own, every run would print a different identifier and the console reporter's
/// golden files could never settle on one.
/// <see cref="NoSkip"/> is the engine half of the <c>--no-skip</c> contrast
/// flag.
/// </remarks>
public sealed record RunRequest
{
    public required IReadOnlyList<IValidationRule> Rules { get; init; }

    public required EffectivePolicy Policy { get; init; }

    public required ValidationStage Stage { get; init; }

    public required BuildTarget Target { get; init; }

    public required DirectoryInfo WorkspaceRoot { get; init; }

    public required IFileSystem FileSystem { get; init; }

    public required IProcessRunner Processes { get; init; }

    /// <summary>
    /// Where cached results live, or <see langword="null"/> for no caching.
    /// </summary>
    /// <remarks>
    /// There is no <c>NoCache</c> flag beside this, deliberately.
    /// <c>--no-cache</c> is the CLI declining to hand the engine a store, which
    /// leaves the engine with one condition instead of two that have to agree —
    /// and two booleans meaning "do not cache" is how a flag ends up being
    /// honoured in one code path and ignored in another.
    /// </remarks>
    public IRuleCacheStore? Cache { get; init; }

    public IReadOnlyList<ChangedFile> ChangedFiles { get; init; } = [];

    public IReadOnlyList<string> PolicyChain { get; init; } = [];

    public string? Pipeline { get; init; }

    /// <summary>
    /// The version of the installed package the policy came from, when one did.
    /// </summary>
    /// <remarks>
    /// Carried through so that the result can say which delivery of the pipeline
    /// produced this verdict. Without it two runs of one commit against two
    /// packages are indistinguishable in every machine-readable output the tool
    /// writes.
    /// </remarks>
    public string? PipelineVersion { get; init; }

    public bool FailOnWarning { get; init; }

    public bool NoSkip { get; init; }

    public Guid? RunId { get; init; }
}
