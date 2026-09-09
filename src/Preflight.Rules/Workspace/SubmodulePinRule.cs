namespace Preflight.Rules;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Reports submodules that are not where the repository says they should be.
/// </summary>
/// <remarks>
/// <para>
/// The second built-in rule to produce a warning as well as a failure, and it
/// produces both from one run. A submodule nobody initialised is one command
/// away from correct and nobody has to decide anything, which is the whole of
/// what a warning means here. A submodule whose checked-out commit is not the
/// one the index records is a different sentence: somebody is about to commit a
/// pointer they did not mean to, or the build is about to compile something
/// else, and no command fixes it without a person choosing which of the two
/// commits is right.
/// </para>
/// <para>
/// It reports "not applicable" rather than failing in a workspace that is not a
/// checkout of the declared client, which is the opposite of what the
/// configuration rule does with the same fact. Nobody configured this rule — it
/// runs everywhere — so failing here would block every exported tarball, every
/// checkout under another client, and every temporary directory a test builds.
/// </para>
/// </remarks>
public sealed class SubmodulePinRule : IValidationRule
{
    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.SubmodulePin,
        DisplayName = "Submodule pin",
        Stage = ValidationStage.Workspace,

        // On the toolchain rule for the reason every rule that starts a child
        // process is: what it needs to know first is that a process can be
        // started at all.
        DependsOn = [BuiltInRuleIds.Toolchain],
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

        var read = await WorkspaceManifestRead.ReadAsync(context, cancellationToken);

        // The manifest is consulted for the client's name and for nothing else,
        // so a manifest that will not parse is left to the rules that read
        // something out of it. This rule falls back to its default and carries
        // on, because a submodule pointing at the wrong commit is worth
        // reporting whatever state the manifest is in.
        var command = read.Manifest?.Vcs?.Command is { Length: > 0 } declared
            ? declared
            : GitCommand.DefaultCommand;

        if (!await GitCommand.IsRepositoryAsync(context, command, cancellationToken))
        {
            return RuleOutcome.NotApplicable();
        }

        var status = await GitCommand.RunAsync(
            context, command, ["submodule", "status", "--recursive"], cancellationToken);

        return status.Match(
            unavailable: RuleOutcome.NotApplicable,
            failed: exitCode => RuleOutcome.Failed(Unreadable(command, exitCode)),
            output: text => Judge(text, cancellationToken));
    }

    private static RuleOutcome Judge(string output, CancellationToken cancellationToken)
    {
        // Empty entries removed, because the output ends in a line terminator
        // and an empty line would otherwise be read as a status character that
        // is not there. Nothing else is trimmed: the leading character is the
        // status, and a space is one of its four values, so trimming the line
        // would turn every up-to-date submodule into an unreadable one.
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var blocking = new List<Finding>();
        var recoverable = new List<Finding>();
        var read = 0;

        foreach (var raw in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Only the carriage return of a two-character terminator, which the
            // split above leaves behind and which is not part of any field.
            var line = raw.TrimEnd('\r');

            if (SubmoduleStatusLine.Parse(line) is not { } entry)
            {
                continue;
            }

            read++;

            switch (entry.State)
            {
                case SubmoduleState.Uninitialised:
                    recoverable.Add(Uninitialised(entry.Path));

                    break;

                case SubmoduleState.Diverged:
                    blocking.Add(Diverged(entry.Path));

                    break;

                case SubmoduleState.Conflicted:
                    blocking.Add(Conflicted(entry.Path));

                    break;

                case SubmoduleState.Unrecognised:
                    blocking.Add(Unrecognised(line));

                    break;

                case SubmoduleState.UpToDate:
                default:
                    break;
            }
        }

        if (read == 0)
        {
            // A repository with no submodules has nothing this rule can be
            // right or wrong about, and a tick would claim submodules were
            // checked when there were none. Counted after parsing rather than
            // before it, so that output consisting only of blank lines answers
            // the same way an empty run does.
            return RuleOutcome.NotApplicable();
        }

        // The blocking findings first. The order of the list is the only
        // hierarchy a rule has, and a warning printed above a failure reads as
        // the more important of the two.
        return blocking.Count > 0
            ? RuleOutcome.Failed([.. blocking, .. recoverable])
            : recoverable.Count > 0
                ? RuleOutcome.Warned([.. recoverable])
                : RuleOutcome.Passed();
    }

    private static Finding Uninitialised(string path) => new()
    {
        Message = "A submodule has never been initialised.",
        Location = new FindingLocation(path),
        Expected = "the recorded commit checked out",
        Actual = "nothing checked out",

        // A command, and nobody has to decide anything: the whole of why this
        // one is a warning and the two below are not.
        Remediation = "Run 'git submodule update --init --recursive'.",
    };

    private static Finding Diverged(string path) => new()
    {
        Message = "A submodule is not at the commit this repository records.",
        Location = new FindingLocation(path),
        Expected = "the recorded commit checked out",
        Actual = "a different commit",
        Remediation =
            "Decide which commit is right: check the recorded one back out, " +
            "or commit the pointer you meant to move.",
    };

    private static Finding Conflicted(string path) => new()
    {
        Message = "A submodule is left in conflict from a merge.",
        Location = new FindingLocation(path),
        Expected = "the recorded commit checked out",
        Actual = "an unresolved merge",
        Remediation = "Resolve the merge inside the submodule, then commit the pointer it should hold.",
    };

    /// <remarks>
    /// The line is quoted because it is the only thing that says what happened,
    /// and it carries a path relative to the workspace root — never an absolute
    /// one, which is what the rest of the output would have added.
    /// </remarks>
    private static Finding Unrecognised(string line) => new()
    {
        Message = "A line of submodule status could not be read.",
        Expected = "a status this tool recognises",
        Actual = FindingText.Truncate(line, LineLimit),
        Remediation =
            "Report the line above: a submodule reported in a form the tool does not know " +
            "is a submodule nobody checked.",
    };

    /// <remarks>
    /// Named for the command and the code, and never for what the client wrote
    /// to standard error — which routinely quotes an absolute path, and every
    /// field here reaches a build log.
    /// </remarks>
    private static Finding Unreadable(string command, int exitCode) => new()
    {
        Message = $"'{command} submodule status' exited with {exitCode}, so no submodule was checked.",
        Expected = "the client able to report on its submodules",
        Actual = $"exit code {exitCode}",
        Remediation = $"Run '{command} submodule status --recursive' in the workspace to see what it says.",
    };

    /// <summary>
    /// How much of an unreadable line is quoted back.
    /// </summary>
    /// <remarks>
    /// A status line is a status character, an object name and a path, so a
    /// hundred and twenty characters holds a real one whole and truncates only
    /// something that was never a status line at all.
    /// </remarks>
    private const int LineLimit = 120;
}
