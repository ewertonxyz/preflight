namespace Preflight.Rules;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Fails when a changed file opens with a line ending it was declared not to
/// have.
/// </summary>
/// <remarks>
/// <para>
/// The concrete failure is a shell script committed with carriage returns,
/// which dies on a Linux agent with a message about a command nobody wrote —
/// and which is invisible in every review, because a diff does not show
/// terminators.
/// </para>
/// <para>
/// The requirement comes from the attributes file first and from policy second,
/// and the order is the decision. The attributes file is a declaration the
/// repository made about itself, so a policy contradicting it would fail a file
/// for obeying its own repository. Policy covers what the declaration does not
/// reach, which is the common case: most repositories never wrote a line about
/// endings at all, and those are exactly the ones where the script arrives with
/// the wrong bytes.
/// </para>
/// <para>
/// Only the first ending in the file is measured. A file mixing both is a
/// different defect whose remedy is a normalisation pass rather than a verdict;
/// the client's own normalisation is all-or-nothing per file; and the question
/// that decides whether a script runs is answered before its first newline.
/// </para>
/// </remarks>
public sealed class LineEndingsRule : IValidationRule
{
    /// <summary>
    /// Where the attributes file is looked for when the policy names none.
    /// </summary>
    /// <remarks>
    /// The name the client itself uses, so the rule reads the file the
    /// repository already has. It is policy rather than a constant because a
    /// fixture — or a repository keeping its attributes somewhere else — needs
    /// to point the rule at a file the surrounding tooling will not also act on.
    /// </remarks>
    public const string DefaultAttributesPath = ".gitattributes";

    /// <summary>
    /// How many opening bytes of a file are read to decide.
    /// </summary>
    /// <remarks>
    /// The answer is at the first newline, and a source file's first line is
    /// tens of bytes; eight thousand covers a generated header block or a long
    /// licence banner without ever reading a whole asset. The files this rule
    /// opens include everything a commit touched, and some of them are
    /// gigabytes.
    /// </remarks>
    public const int DefaultProbeBytes = 8192;

    /// <summary>
    /// The smallest window that can answer anything.
    /// </summary>
    /// <remarks>
    /// Two bytes, because telling a bare line feed from a carriage return and
    /// line feed needs the byte before the newline. Without a floor,
    /// <c>maxProbeBytes: 0</c> turns the rule off while leaving it reporting
    /// that it looked — which is the silent failure the whole set exists to
    /// prevent.
    /// </remarks>
    public const int MinimumProbeBytes = 2;

    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.LineEndings,
        DisplayName = "Line endings",
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

        var attributes = await ReadAttributesAsync(context, cancellationToken);

        var required = context.Policy.GetValue("lf", Array.Empty<string>())
            .Select(GlobPattern.Compile)
            .ToArray();

        if (attributes is not { DeclaresAnyEol: true } && required.Length == 0)
        {
            // Neither source has anything to say, so there is no requirement to
            // measure against. Answering "passed" here would claim every
            // changed file had been checked against a rule nobody wrote.
            return RuleOutcome.NotApplicable();
        }

        var probeBytes = Math.Max(
            (int)context.Policy.GetValue("maxProbeBytes", (long)DefaultProbeBytes),
            MinimumProbeBytes);

        var scan = new ChangedFileScan();

        foreach (var file in context.ChangedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.Kind == ChangeKind.Deleted)
            {
                continue;
            }

            if (Requirement(attributes, required, file.RelativePath) is not { } expected)
            {
                continue;
            }

            // The change set describes a diff, not the disk. A file deleted
            // after the reference it was diffed against, or renamed only in
            // case on a case-insensitive volume, is named here and not there —
            // and opening it would error a perfectly ordinary commit.
            var path = Path.Combine(context.WorkspaceRoot.FullName, file.RelativePath);

            if (!context.FileSystem.FileExists(path))
            {
                continue;
            }

            var probe = TextProbe.Of(
                await TextProbe.ReadHeadAsync(context, path, probeBytes, cancellationToken));

            if (probe.IsBinary || probe.FirstEnding == LineEnding.None)
            {
                // Counted as examined by neither, because neither was. A file
                // with no newline in the window has no ending to be right or
                // wrong about, and a binary file was never text — calling
                // either of them "passed" is a tick over a file nobody read.
                continue;
            }

            // Counted last, after every decision not to look. Counting at the
            // top of the loop would make a commit of nothing but binaries
            // report that its line endings had been checked.
            scan.Examines(file);

            if (probe.FirstEnding != expected)
            {
                scan.Report(Describe(file.RelativePath, expected, probe.FirstEnding));
            }
        }

        return scan.Outcome();
    }

    /// <remarks>
    /// A missing attributes file is not the end of the rule, which it would be
    /// if this returned early. Half the requirement lives in policy, and a
    /// repository with no attributes file at all is precisely the one that
    /// needs the policy half.
    /// </remarks>
    private static async Task<GitAttributes?> ReadAttributesAsync(
        RuleContext context,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(
            context.WorkspaceRoot.FullName,
            context.Policy.GetValue("attributesPath", DefaultAttributesPath));

        return context.FileSystem.FileExists(path)
            ? GitAttributes.Parse(await context.FileSystem.ReadAllTextAsync(path, cancellationToken))
            : null;
    }

    /// <summary>
    /// Which ending this path has to have, if any source says so.
    /// </summary>
    /// <returns>
    /// The required ending, or <see langword="null"/> when the file is not to be
    /// examined at all.
    /// </returns>
    private static LineEnding? Requirement(
        GitAttributes? attributes,
        IReadOnlyList<GlobPattern> required,
        string relativePath) =>
        attributes?.EolFor(relativePath) switch
        {
            EolRequirement.Lf => LineEnding.Lf,
            EolRequirement.Crlf => LineEnding.Crlf,

            // A path the repository declared has no text handling is not
            // measured, and the policy list does not get to overrule that:
            // the repository has already answered the question, and a rule
            // saying otherwise leaves the reader no fix but to edit policy.
            EolRequirement.Exempt => null,
            _ => required.Any(pattern => pattern.Matches(relativePath)) ? LineEnding.Lf : null,
        };

    /// <remarks>
    /// The path, the position and the two endings — never a byte of the file.
    /// This rule opens whatever a commit touched, and a message quoting the
    /// opening line would publish it to everyone who can read a build log.
    /// </remarks>
    private static Finding Describe(string relativePath, LineEnding expected, LineEnding actual) => new()
    {
        Message = "A changed file opens with the line ending it was declared not to have.",
        Location = new FindingLocation(relativePath, Line: 1),
        Expected = Name(expected),
        Actual = Name(actual),
        Remediation =
            $"Rewrite the file with {Name(expected)} endings and commit it again. " +
            "If it is meant to have the other kind, say so in the attributes file.",
    };

    private static string Name(LineEnding ending) => ending == LineEnding.Crlf ? "CRLF" : "LF";
}
