namespace Preflight.Rules;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Fails when a changed path will not survive being checked out on another
/// operating system.
/// </summary>
/// <remarks>
/// <para>
/// Four independent checks, each its own type, composed into an ordered list
/// the rule calls as though it were one. A fifth is a new class and one entry
/// in <see cref="DefaultChecks"/> rather than another branch here, and the
/// policy can turn any one of them off by name instead of needing a boolean per
/// check.
/// </para>
/// <para>
/// The one rule of this set that ships with real limits and will therefore
/// report on a repository nobody has configured. That is deliberate: a rule
/// nobody switches on is a rule that does not exist, and the limits are policy
/// precisely so that a production which needs different ones changes a line
/// rather than disabling the rule.
/// </para>
/// </remarks>
public sealed class PathPortabilityRule : IValidationRule
{
    /// <summary>
    /// The checks that run when the policy names none: all of them.
    /// </summary>
    /// <remarks>
    /// Every check by default, because each one describes a path that provably
    /// cannot be checked out somewhere. A default that ran a subset would be
    /// the tool deciding which operating systems a production cares about.
    /// </remarks>
    public static readonly string[] DefaultChecks =
    [
        CaseCollisionCheck.CheckName,
        InvalidCharacterCheck.CheckName,
        ReservedNameCheck.CheckName,
        PathLengthCheck.CheckName,
    ];

    /// <summary>
    /// The longest path, relative to the workspace root, accepted by default.
    /// </summary>
    /// <remarks>
    /// Windows' classic limit for a fully qualified path. Applied to the
    /// relative path it is deliberately conservative — the checkout directory
    /// is added on top of it — and that is the trade: a number that is the same
    /// on every machine, slightly stricter than the one that actually breaks.
    /// </remarks>
    public const int DefaultMaxPathLength = 260;

    /// <summary>
    /// The longest single file or directory name accepted by default.
    /// </summary>
    /// <remarks>
    /// What nearly every filesystem in use allows for one component, and the
    /// limit that long-path support on Windows does not lift. A path can be
    /// made shorter by moving it; a component this long has to be renamed.
    /// </remarks>
    public const int DefaultMaxComponentLength = 255;

    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.PathPortability,
        DisplayName = "Path portability",
        Stage = ValidationStage.PreSubmit,
        DefaultBlocking = true,

        // Nothing depends on this rule, so gating would change nothing whatever
        // it said. Written out anyway, because the descriptor's own default is
        // true and a reader finding it inherited cannot tell a decision from an
        // omission.
        DefaultGating = false,
    };

    public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        var selected = context.Policy.GetValue("checks", DefaultChecks);

        // An empty list is somebody saying in writing that nothing is to be
        // checked, which is a different fact from a commit with nothing in it —
        // but both mean this rule measured nothing.
        if (selected.Length == 0 || context.ChangedFiles.Count == 0)
        {
            return Task.FromResult(RuleOutcome.NotApplicable());
        }

        var all = Compose(context);
        var unknown = selected.Where(name => !all.Any(check => check.Name == name)).ToArray();

        if (unknown.Length > 0)
        {
            // Loudly, and against the policy rather than a file. Settings are
            // opaque to the loader by contract, so a misspelled name reaches
            // here or nowhere — and silently running one check fewer would leave
            // a green report over a check that never ran.
            return Task.FromResult(RuleOutcome.Failed(Misconfigured(unknown)));
        }

        var limits = new PathPortabilityLimits(
            (int)context.Policy.GetValue("maxPathLength", (long)DefaultMaxPathLength),
            (int)context.Policy.GetValue("maxComponentLength", (long)DefaultMaxComponentLength));

        var scan = new ChangedFileScan();

        // The paths are gathered before any check runs, because two of the four
        // compare paths with each other rather than examining them one at a
        // time.
        var paths = context.ChangedFiles.Where(scan.Examines).Select(file => file.RelativePath).ToArray();

        var running = all.Where(check => selected.Contains(check.Name)).ToArray();

        foreach (var check in running)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var defect in check.Inspect(paths, limits))
            {
                scan.Report(Describe(defect, check.Name));
            }
        }

        return Task.FromResult(scan.Outcome());
    }

    /// <summary>
    /// The checks, in the order their findings are reported.
    /// </summary>
    /// <remarks>
    /// Composed rather than switched on. A fifth check is a class and an entry
    /// in <see cref="DefaultChecks"/>, and nothing in this method changes.
    /// </remarks>
    private static IReadOnlyList<IPathPortabilityCheck> Compose(RuleContext context) =>
    [
        new CaseCollisionCheck(context.FileSystem, context.WorkspaceRoot),
        new InvalidCharacterCheck(),
        new ReservedNameCheck(),
        new PathLengthCheck(),
    ];

    /// <remarks>
    /// The finding names the policy key and the entry, and lists the names that
    /// do exist. It carries no location, because nothing is wrong with the
    /// workspace — sending the reader to a file would send them to the wrong
    /// place entirely.
    /// </remarks>
    private static Finding Misconfigured(IReadOnlyList<string> unknown) => new()
    {
        Message = "The 'checks' setting names a check that does not exist.",
        Expected = $"names from: {string.Join(", ", DefaultChecks)}",
        Actual = string.Join(", ", unknown),
        Remediation = "Correct 'checks' for this rule in the pipeline's policy, or remove it to run every check.",
    };

    /// <remarks>
    /// The rule writes the remediation and the check writes what it saw. A
    /// check that wrote its own remedy would put four voices in one report, and
    /// the remedy depends on limits the check was handed rather than chose.
    /// </remarks>
    private static Finding Describe(PathDefect defect, string check) => new()
    {
        Message = $"Changed path is not portable: it {defect.Problem}.",
        Location = new FindingLocation(defect.Path),
        Expected = defect.Expected,
        Actual = defect.Path,
        Remediation =
            $"Rename or move the path so it satisfies the '{check}' check, or ask the pipeline's " +
            "author to adjust that check's settings for this rule.",
    };
}
