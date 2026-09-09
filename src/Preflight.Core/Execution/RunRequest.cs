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
/// <see cref="NoSkip"/> is the tool half of the <c>--no-skip</c> contrast
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
    /// Reads free space on the volumes the workspace declares.
    /// </summary>
    /// <remarks>
    /// Required here and optional on the rule context, and the asymmetry is the
    /// point. The context is the plugin contract and cannot demand a new member
    /// without breaking every author who builds one in a test; this record is
    /// assembled only by whoever hosts the tool, so requiring it makes a host
    /// that forgot the probe fail to compile rather than serve "not applicable"
    /// forever.
    /// </remarks>
    public required IVolumeProbe Volumes { get; init; }

    /// <summary>
    /// Reads the environment block the tool was started with.
    /// </summary>
    /// <remarks>
    /// Required here and optional on the rule context, for the reason the
    /// volume probe above is: this record is assembled only by whoever hosts
    /// the tool, so requiring it makes a host that forgot to wire the probe
    /// fail to compile rather than leave the rule that needs it reporting "not
    /// applicable" forever.
    /// </remarks>
    public required IEnvironmentProbe Environment { get; init; }

    /// <summary>
    /// Where cached results live, or <see langword="null"/> for no caching.
    /// </summary>
    /// <remarks>
    /// There is no <c>NoCache</c> flag beside this, deliberately.
    /// <c>--no-cache</c> is the CLI declining to hand the tool a store, which
    /// leaves the tool with one condition instead of two that have to agree —
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
