namespace Preflight.Rules;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Fails when a changed file still carries a conflict marker nobody resolved.
/// </summary>
/// <remarks>
/// <para>
/// One of two built-in rules that report without being configured first, and
/// the criterion that lets it is written down so the next rule of this kind has
/// something to be measured against: what it looks for has to be something
/// nobody ever does on purpose, and the false-positive rate has to be arguable
/// as zero rather than as low. Nobody names a file after a reserved device by
/// accident either, which is what the other one relies on.
/// </para>
/// <para>
/// That criterion is also what decides the shape of this one. Only the two
/// directional markers are reported. A line of equals signs is how a heading is
/// underlined in two of the most common markup formats there are, so reporting
/// it would fail a correct file — and a rule that fails something correct is
/// switched off whole rather than adjusted.
/// </para>
/// <para>
/// A marker has to begin the line. Found anywhere else it is a document about
/// conflict markers, a test fixture, or a comment, all of which are deliberate.
/// </para>
/// </remarks>
public sealed class MergeArtifactRule : IValidationRule
{
    /// <summary>
    /// The markers that have no legitimate use at the start of a line.
    /// </summary>
    /// <remarks>
    /// The two directional ones and the common-ancestor one, which the client
    /// writes only in a three-way conflict and which nothing else produces.
    /// A line of equals signs is deliberately absent, and it is the one of the
    /// four that has another use.
    /// </remarks>
    public static readonly string[] Markers = ["<<<<<<<", ">>>>>>>", "|||||||"];

    /// <summary>
    /// How large a file may be before it is left unexamined.
    /// </summary>
    /// <remarks>
    /// Five mebibytes. This rule runs unconfigured over everything a commit
    /// touched, so it is the one most likely to be handed something enormous —
    /// and a conflict marker lives in a file somebody was editing by hand,
    /// which is not what a file that size is.
    /// </remarks>
    public const long DefaultMaxBytes = 5 * 1024 * 1024;

    /// <summary>
    /// How many opening bytes decide whether a file is binary.
    /// </summary>
    public const int ProbeBytes = 8192;

    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.MergeArtifact,
        DisplayName = "Merge artifact",
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

        var maxBytes = context.Policy.GetValue("maxBytes", DefaultMaxBytes);
        var scan = new ChangedFileScan();

        foreach (var file in context.ChangedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.Kind == ChangeKind.Deleted)
            {
                continue;
            }

            var path = Path.Combine(context.WorkspaceRoot.FullName, file.RelativePath);

            // The change set describes a diff, not the disk, so a file named in
            // it may not be there to open.
            if (!context.FileSystem.FileExists(path) || context.FileSystem.GetFileSize(path) > maxBytes)
            {
                continue;
            }

            var bytes = await TextProbe.ReadHeadAsync(context, path, (int)maxBytes, cancellationToken);

            if (TextProbe.Of(bytes.AsSpan(0, Math.Min(bytes.Length, ProbeBytes))).IsBinary)
            {
                continue;
            }

            // Counted last, after every decision not to look, so that a commit
            // of nothing but binaries reports that nothing was examined rather
            // than that everything passed.
            scan.Examines(file);

            Examine(scan, file.RelativePath, TextProbe.DecodeText(bytes), cancellationToken);
        }

        return scan.Outcome();
    }

    private static void Examine(
        ChangedFileScan scan,
        string relativePath,
        string text,
        CancellationToken cancellationToken)
    {
        var line = 0;
        var start = 0;
        var remaining = text.AsSpan();

        // Walked by index rather than split into an array. The file has already
        // been read whole, and splitting it would hold a second copy of it,
        // line by line, for a scan that only ever looks at the opening of each.
        while (start <= remaining.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            line++;

            var end = remaining[start..].IndexOf('\n');
            var candidate = (end < 0 ? remaining[start..] : remaining.Slice(start, end)).TrimEnd('\r');

            foreach (var marker in Markers)
            {
                if (candidate.StartsWith(marker, StringComparison.Ordinal))
                {
                    scan.Report(Describe(relativePath, line, marker));

                    break;
                }
            }

            if (end < 0)
            {
                break;
            }

            start += end + 1;
        }
    }

    /// <remarks>
    /// The marker and the position, never the conflicted text around it. What
    /// is inside a conflict is the content of two branches, and every field here
    /// reaches a build log that far more people read than ran the build.
    /// </remarks>
    private static Finding Describe(string relativePath, int line, string marker) => new()
    {
        Message = "A changed file still carries a merge conflict marker.",
        Location = new FindingLocation(relativePath, line),
        Expected = "a resolved file",
        Actual = $"a line beginning '{marker}'",
        Remediation = "Finish resolving the conflict, remove the markers, and commit the file again.",
    };
}
