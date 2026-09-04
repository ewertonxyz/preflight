namespace Preflight.Cli.Model;

using Preflight.Abstractions.Model;
using Preflight.Core.Policy;

/// <summary>
/// One invocation, already parsed and validated.
/// </summary>
/// <remarks>
/// The boundary between parsing and doing. Everything the parser refuses has
/// been refused before one of these exists, so a command reading it never has
/// to ask whether the user meant something else — which is what keeps the
/// refusals in one place instead of repeated at each use.
/// </remarks>
public sealed record RunOptions
{
    public required ValidationStage Stage { get; init; }

    public required StatedBuildTarget Target { get; init; }

    public string? Pipeline { get; init; }

    public string? ChangedFrom { get; init; }

    public ReportFormat Format { get; init; } = ReportFormat.Console;

    public bool NoSkip { get; init; }

    public bool FailOnWarning { get; init; }

    public bool NoUnicode { get; init; }

    /// <summary>
    /// <c>--no-cache</c>: ignore the incremental cache and re-execute.
    /// </summary>
    /// <remarks>
    /// The tool has no flag of its own for this. The CLI expresses it by not
    /// handing <c>RunRequest</c> a cache store at all, so there is one
    /// condition downstream rather than two that have to agree.
    /// </remarks>
    public bool NoCache { get; init; }

    public bool NoLocal { get; init; }

    public bool AllowLocal { get; init; }

    public IReadOnlyList<string> SetOverrides { get; init; } = [];

    /// <summary>
    /// Fixes the run id, so a golden file can exist.
    /// </summary>
    /// <remarks>
    /// The tool promises byte-identical output for identical input, and a fresh
    /// <see cref="Guid"/> per run makes that literally impossible. Not a
    /// command-line flag: it exists for tests, and a flag would invite someone
    /// to pin it in a pipeline and lose the one field that distinguishes two
    /// runs in the history.
    /// </remarks>
    public Guid? RunId { get; init; }
}
