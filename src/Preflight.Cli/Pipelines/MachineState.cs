namespace Preflight.Cli.Pipelines;

/// <summary>
/// What this machine remembers between runs.
/// </summary>
/// <remarks>
/// Two facts, and no more. The pins answer "which version of each pipeline does
/// this machine use", and the retention count answers "how many old versions is
/// this disk willing to keep". Both are machine-scoped: a pipeline deciding how
/// much disk somebody else's laptop spends would be a policy file reaching
/// outside the run it configures.
/// </remarks>
public sealed record MachineState
{
    /// <summary>The default number of versions kept per pipeline.</summary>
    /// <remarks>
    /// Ten rather than two or three, and the number is doing work. Retention
    /// knows about the pins and about the workspace in front of it, and
    /// deliberately knows nothing about other checkouts on the same disk — so a
    /// version another clone requires can be collected. Ten is the margin that
    /// makes that recoverable by reinstalling rather than common enough to
    /// notice.
    /// </remarks>
    public const int DefaultKeep = 10;

    /// <summary>
    /// The version pinned per pipeline name.
    /// </summary>
    /// <remarks>
    /// Keyed ignoring case, because the name becomes a directory on a file
    /// system that does not distinguish case. An ordinal dictionary over a
    /// case-insensitive disk makes a pin that exists and is not found, and the
    /// run then falls to the newest installed version without a word — the
    /// wrong-package run, arrived at by a dictionary comparer.
    /// </remarks>
    public required IReadOnlyDictionary<string, PackageVersion> Pins { get; init; }

    /// <summary>How many versions of each pipeline the install root keeps.</summary>
    public required int Keep { get; init; }

    /// <summary>The state of a machine that has never installed anything.</summary>
    public static MachineState Empty { get; } = new()
    {
        Pins = new Dictionary<string, PackageVersion>(StringComparer.OrdinalIgnoreCase),
        Keep = DefaultKeep,
    };
}
