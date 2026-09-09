namespace Preflight.Rules;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Fails when a changed file the attributes file sends to LFS was committed as
/// a real blob instead of a pointer.
/// </summary>
/// <remarks>
/// The one rule in the pre-submit set whose damage grows after the commit. A
/// forbidden path or an oversized file is removed by a later commit; a large
/// binary that entered the history because the filter did not run comes out
/// only by rewriting history in every clone of the repository.
/// </remarks>
public sealed class LfsPointerRule : IValidationRule
{
    /// <summary>
    /// Where the attributes file is looked for when the policy names none.
    /// </summary>
    /// <remarks>
    /// The name Git itself uses, so the rule reads the file the repository
    /// already has. It is policy rather than a constant because a fixture — or
    /// a repository keeping its attributes somewhere else — needs to point the
    /// rule at a file the surrounding tooling will not also act on.
    /// </remarks>
    public const string DefaultAttributesPath = ".gitattributes";

    /// <summary>
    /// How many opening bytes of a file are read to decide.
    /// </summary>
    /// <remarks>
    /// A pointer file is a few hundred bytes and the answer is in its first
    /// forty-two, so nothing is gained by reading further — and the files this
    /// rule opens are exactly the ones that may be hundreds of megabytes.
    /// Sixty-four leaves room for the preamble and for a reader to see it is
    /// not a coincidence.
    /// </remarks>
    public const int DefaultProbeBytes = 64;

    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.LfsPointer,
        DisplayName = "LFS pointer",
        Stage = ValidationStage.PreSubmit,
        DefaultBlocking = true,

        // Nothing depends on this rule, so gating would change nothing whatever
        // it said. Written out anyway, because the descriptor's own default is
        // true and a reader finding it inherited cannot tell a decision from an
        // omission.
        DefaultGating = false,
    };

    public async Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var attributesPath = Path.Combine(
            context.WorkspaceRoot.FullName,
            context.Policy.GetValue("attributesPath", DefaultAttributesPath));

        if (!context.FileSystem.FileExists(attributesPath))
        {
            // No attributes file is not a failure. A repository that does not
            // use LFS has nothing for this rule to be right or wrong about, and
            // the rule that fails on a missing file is the toolchain one, which
            // reads a file the workspace is required to have.
            return RuleOutcome.NotApplicable();
        }

        var attributes = GitAttributes.Parse(
            await context.FileSystem.ReadAllTextAsync(attributesPath, cancellationToken));

        if (!attributes.DeclaresAnyLfsPattern)
        {
            return RuleOutcome.NotApplicable();
        }

        var probeBytes = Math.Max(
            (int)context.Policy.GetValue("maxProbeBytes", (long)DefaultProbeBytes),
            LfsPointer.PreambleLength);

        var scan = new ChangedFileScan();

        foreach (var file in context.ChangedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!attributes.SendsToLfs(file.RelativePath))
            {
                continue;
            }

            // The new path, never the old one. A rename's PreviousRelativePath
            // names a file that is no longer there, and a deleted file is not
            // examined at all — removing a mistakenly committed blob is the
            // fix, not another violation.
            var path = Path.Combine(context.WorkspaceRoot.FullName, file.RelativePath);

            if (!context.FileSystem.FileExists(path) || !scan.Examines(file))
            {
                continue;
            }

            if (!await IsPointerAsync(context, path, probeBytes, cancellationToken))
            {
                scan.Report(Describe(file.RelativePath));
            }
        }

        return scan.Outcome();
    }

    /// <remarks>
    /// The limited read belongs to the shared probe, which several rules make
    /// and which has the loop that survives a stream returning fewer bytes than
    /// it was asked for. A second copy of that loop here would be a second place
    /// the short read can come back, and only one of the two would have a test.
    /// </remarks>
    private static async Task<bool> IsPointerAsync(
        RuleContext context,
        string path,
        int probeBytes,
        CancellationToken cancellationToken) =>
        LfsPointer.Recognises(
            await TextProbe.ReadHeadAsync(context, path, probeBytes, cancellationToken));

    /// <remarks>
    /// The path and never the content. This rule opens files whose whole point
    /// is that they are content, and everything written here reaches the
    /// console, the build log and the run's stored history — so quoting even
    /// the opening line would publish it to everyone who can read a build.
    /// </remarks>
    private static Finding Describe(string relativePath) => new()
    {
        Message = "A file tracked by LFS was committed as a real blob.",
        Location = new FindingLocation(relativePath),
        Expected = "an LFS pointer file",
        Actual = "file content",
        Remediation =
            "Install the LFS filter and re-add the file so it is replaced by a pointer. " +
            "If this file is not meant to be tracked, take it out of the attributes file.",
    };
}
