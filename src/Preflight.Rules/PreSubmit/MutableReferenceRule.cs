namespace Preflight.Rules;

using System.Text.RegularExpressions;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Fails when a changed file refers to a third-party artefact by a name that
/// can move.
/// </summary>
/// <remarks>
/// <para>
/// A pipeline step pinned to a tag, an image pinned to <c>latest</c>, a tool
/// with no version: the difference between a build that reproduces and a build
/// that turned red overnight with nothing committed in between.
/// </para>
/// <para>
/// Where to look and what counts as a reference are both policy, and the
/// expression is the runtime's rather than a pattern language of this project's
/// own. This is the only surface of the product where policy carries an
/// expression, and it has to stay the only one. The alternative was a fixed set
/// of known shapes, which would teach the tool what a pipeline step is and what
/// a container file is — the same boundary that keeps the companion rule from
/// knowing what an asset is. A studio with a format of its own writes one line
/// instead of opening an issue.
/// </para>
/// <para>
/// The rope that buys is paid for before a single file is opened: every entry
/// is parsed, every expression compiled and checked for exactly one capturing
/// group, and a failure names the setting and the entry rather than a file in
/// the workspace, because nothing is wrong with the workspace.
/// </para>
/// </remarks>
public sealed class MutableReferenceRule : IValidationRule
{
    /// <summary>
    /// What a captured reference has to look like when the policy names no
    /// other shape.
    /// </summary>
    /// <remarks>
    /// A forty-character object name, lower case, anchored at both ends. Lower
    /// case because that is what a client prints, so accepting upper case would
    /// be accepting whatever somebody happened to paste. The end anchor is
    /// <c>\z</c> and not <c>$</c>, which in this runtime also matches before a
    /// final newline — and a capture that ran on into the next line would pass
    /// the check whose entire purpose is to refuse a reference that is not
    /// exactly an object name.
    /// </remarks>
    public const string DefaultImmutablePattern = @"\A[0-9a-f]{40}\z";

    /// <summary>
    /// How large a file may be before it is left unexamined.
    /// </summary>
    /// <remarks>
    /// Five mebibytes. A path pattern from policy can match anything a commit
    /// touched, and this rule reads a matched file whole in order to apply an
    /// expression to it — so without a ceiling a glob written a little too
    /// broadly turns a package into an out-of-memory failure, which the tool
    /// would report as its own defect rather than as the policy it is.
    /// </remarks>
    public const long DefaultMaxBytes = 5 * 1024 * 1024;

    /// <summary>
    /// How many opening bytes decide whether a matched file is binary.
    /// </summary>
    public const int ProbeBytes = 8192;

    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.MutableReference,
        DisplayName = "Mutable reference",
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

        var entries = context.Policy.GetValue("pinned", Array.Empty<string>());

        if (entries.Length == 0)
        {
            // No default entries, and that is a decision. Which third-party
            // formats a production refers to is a fact about its pipeline, and
            // a plausible-looking default here would be somebody's policy
            // written in C# — which the next person turns the whole rule off to
            // escape.
            return RuleOutcome.NotApplicable();
        }

        var immutable = PinnedReference.Compile(
            context.Policy.GetValue("immutable", DefaultImmutablePattern));

        var malformed = entries.Where(entry => PinnedReference.Parse(entry) is null).ToArray();

        if (immutable is null || malformed.Length > 0)
        {
            // Against the policy, before any file is opened. A typo reported
            // once beats the same typo reported against every file that
            // happened to match it — and it is a failure rather than an errored
            // rule, because the mistake is in what somebody wrote and not in
            // the tool that read it.
            return RuleOutcome.Failed(
                immutable is null ? MisconfiguredShape() : MisconfiguredEntries(malformed));
        }

        var references = entries.Select(entry => PinnedReference.Parse(entry)!).ToArray();
        var maxBytes = context.Policy.GetValue("maxBytes", DefaultMaxBytes);
        var scan = new ChangedFileScan();

        foreach (var file in context.ChangedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.Kind == ChangeKind.Deleted)
            {
                continue;
            }

            var matched = references.Where(reference => reference.When.Matches(file.RelativePath)).ToArray();
            var path = Path.Combine(context.WorkspaceRoot.FullName, file.RelativePath);

            // The change set describes a diff, not the disk, so a file named in
            // it may not be there to open.
            if (matched.Length == 0 || !context.FileSystem.FileExists(path))
            {
                continue;
            }

            if (context.FileSystem.GetFileSize(path) > maxBytes)
            {
                continue;
            }

            var bytes = await TextProbe.ReadHeadAsync(context, path, (int)maxBytes, cancellationToken);

            if (TextProbe.Of(bytes.AsSpan(0, Math.Min(bytes.Length, ProbeBytes))).IsBinary)
            {
                continue;
            }

            // Counted last, after every decision not to look, so that a commit
            // of nothing but oversized or binary files reports that nothing was
            // examined rather than that everything passed.
            scan.Examines(file);

            Examine(scan, file.RelativePath, TextProbe.DecodeText(bytes), matched, immutable, cancellationToken);
        }

        return scan.Outcome();
    }

    private static void Examine(
        ChangedFileScan scan,
        string relativePath,
        string text,
        IReadOnlyList<PinnedReference> references,
        Regex immutable,
        CancellationToken cancellationToken)
    {
        // In the order the policy declared them, so that a file matching two
        // entries is reported the way the reader wrote them down.
        foreach (var reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (Match match in reference.Expression.Matches(text))
            {
                var capture = match.Groups[1];

                if (immutable.IsMatch(capture.Value))
                {
                    continue;
                }

                scan.Report(Describe(relativePath, LineOf(text, capture.Index), capture.Value, reference.Text));
            }
        }
    }

    /// <remarks>
    /// Line feeds only, so that a file with carriage returns is numbered the
    /// same way every editor numbers it. One-based, because a report that said
    /// line zero would send the reader to a line no editor has.
    /// </remarks>
    private static int LineOf(string text, int index) =>
        text.AsSpan(0, index).Count('\n') + 1;

    /// <remarks>
    /// Names the setting and the entry rather than a file, because nothing is
    /// wrong with the workspace — pointing at a file would send the reader to
    /// fix something that is perfectly correct.
    /// </remarks>
    private static Finding MisconfiguredEntries(IReadOnlyList<string> entries) => new()
    {
        Message = "An entry in 'pinned' is not a reference rule.",
        Expected =
            $"'<pattern> {PinnedReference.Arrow} <expression>', where the expression compiles " +
            "and has exactly one capturing group",
        Actual = string.Join(", ", entries),
        Remediation =
            "Correct 'pinned' for this rule in the pipeline's policy. Backreferences and " +
            "lookaround are not available here, and an entry using either will not compile.",
    };

    private static Finding MisconfiguredShape() => new()
    {
        Message = "The 'immutable' setting is not an expression that compiles.",
        Expected = "an expression a captured reference has to match",
        Actual = "an expression that will not compile",
        Remediation =
            "Correct 'immutable' for this rule in the pipeline's policy, or remove it to use " +
            "the default shape.",
    };

    /// <remarks>
    /// The capture is quoted and nothing else from the file is. What was
    /// captured is the whole of what the reader has to change, and it is bounded
    /// by the shared cap because an expression written a little too broadly can
    /// capture a great deal more than a version.
    /// </remarks>
    private static Finding Describe(string relativePath, int line, string capture, string entry) => new()
    {
        Message = "A reference to a third-party artefact is pinned to something that can move.",
        Location = new FindingLocation(relativePath, line),
        Expected = "a reference that names one immutable version",
        Actual = FindingText.Truncate(capture, CaptureLimit),
        Remediation =
            $"Pin it to an immutable version, or ask the pipeline's author to change the '{entry}' " +
            "entry in 'pinned' for this rule.",
    };

    /// <summary>
    /// How much of a capture is quoted back.
    /// </summary>
    /// <remarks>
    /// A reference is a version or an object name, so a hundred and twenty
    /// characters holds a real one whole and truncates only an expression that
    /// captured more of the file than its author meant it to.
    /// </remarks>
    private const int CaptureLimit = 120;
}
