namespace Preflight.Rules;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Fails when the version control client is not configured the way the
/// workspace declares it has to be.
/// </summary>
/// <remarks>
/// <para>
/// It fails rather than warns, and the distinction from the unrestored
/// dependency beside it is the tense. A dependency nobody restored warns about
/// a step that has not happened yet and that the build will run anyway; a
/// misconfigured client describes a state that has already had its effect —
/// every commit made under it entered the history with the wrong line endings,
/// or entered as a blob because the filter never ran. A warning about something
/// already done is the quadrant people learn to scroll past.
/// </para>
/// <para>
/// The settings are read with the client's own resolution, not the
/// repository's. The question is whether <em>this</em> client will handle
/// <em>this</em> checkout, and long paths have to be on for the client that
/// runs wherever it was written down — so a value inherited from the machine's
/// global configuration is a pass, and the remedy names the global scope so the
/// reader knows which one to change.
/// </para>
/// </remarks>
public sealed class VcsConfigurationRule : IValidationRule
{
    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.VcsConfiguration,
        DisplayName = "Version control configuration",
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

        if (read.Malformed is { } malformed)
        {
            return RuleOutcome.Failed(malformed);
        }

        if (read.Manifest?.Vcs is not { Settings.Count: > 0 } requirement)
        {
            // No declaration is nothing to be right or wrong about. A missing
            // manifest is already reported loudly by the toolchain rule this
            // one depends on, and saying it again would put one problem on two
            // lines.
            return RuleOutcome.NotApplicable();
        }

        var command = string.IsNullOrWhiteSpace(requirement.Command)
            ? GitCommand.DefaultCommand
            : requirement.Command;

        if (!await GitCommand.IsRepositoryAsync(context, command, cancellationToken))
        {
            // A failure, where the submodule rule answers "not applicable" to
            // the same fact. The asymmetry is the decision: here the workspace
            // declared a version control requirement, so a checkout that cannot
            // satisfy it is a workspace that is wrong. There nobody declared
            // anything and the rule runs everywhere.
            return RuleOutcome.Failed(NotARepository(command));
        }

        var findings = new List<Finding>();

        foreach (var setting in requirement.Settings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Describe(setting) is { } refused)
            {
                findings.Add(refused);

                continue;
            }

            if (await CheckAsync(context, command, setting, cancellationToken) is { } finding)
            {
                findings.Add(finding);
            }
        }

        return findings.Count > 0 ? RuleOutcome.Failed([.. findings]) : RuleOutcome.Passed();
    }

    /// <summary>
    /// Refuses a setting name the client would read as an option.
    /// </summary>
    /// <remarks>
    /// An argument list stops a shell from seeing the value; it does not stop
    /// the client itself from parsing a leading dash as one of its own flags,
    /// and the configuration command has no separator that would end its
    /// options. A name that would be misread is refused against the manifest,
    /// which is where the mistake is.
    /// </remarks>
    private static Finding? Describe(VcsSetting setting) =>
        string.IsNullOrWhiteSpace(setting.Name) || setting.Name.StartsWith('-')
            ? new Finding
            {
                Message = "A declared version control setting has no usable name.",
                Expected = "a setting name the client can read",
                Actual = string.IsNullOrWhiteSpace(setting.Name) ? "an empty name" : setting.Name,
                Remediation = "Correct the entry under 'vcs' in the workspace manifest.",
            }
            : null;

    private static async Task<Finding?> CheckAsync(
        RuleContext context,
        string command,
        VcsSetting setting,
        CancellationToken cancellationToken)
    {
        var result = await GitCommand.RunAsync(
            context, command, ["config", "--get", setting.Name], cancellationToken);

        return result.Match(
            unavailable: () => Unavailable(command, setting),
            failed: exitCode => exitCode == AmbiguousExitCode
                ? Ambiguous(command, setting)
                : Missing(command, setting),
            // Trimmed here rather than by the collaborator, which serves a
            // second question whose answer begins with a meaningful space.
            output: value => Compare(command, setting, value.Trim()));
    }

    /// <summary>
    /// What the client exits with when a setting has more than one value.
    /// </summary>
    /// <remarks>
    /// A third state, and folding it into "not set" would send the reader
    /// looking for a missing setting that is in fact there twice. The number is
    /// the client's, which is why it is named here rather than written into the
    /// comparison.
    /// </remarks>
    private const int AmbiguousExitCode = 2;

    private static Finding? Compare(string command, VcsSetting setting, string value)
    {
        if (setting.Expected is null)
        {
            return value.Length == 0 ? Missing(command, setting) : null;
        }

        return string.Equals(value, setting.Expected, StringComparison.Ordinal)
            ? null
            : new Finding
            {
                Message = $"'{setting.Name}' is not set to what this workspace requires.",
                Expected = setting.Expected,

                // Filled only because the manifest declared what it expected.
                // Whoever wrote the expectation asked for the comparison; a
                // check for mere presence prints the name and nothing else, so
                // that a personal address or a path carrying an account name
                // cannot leave through this rule.
                Actual = value.Length == 0 ? "not set" : value,
                Remediation = $"Run '{command} config --global {setting.Name} {setting.Expected}'.",
            };
    }

    private static Finding Missing(string command, VcsSetting setting) => new()
    {
        Message = $"'{setting.Name}' is not set in this client's configuration.",
        Expected = setting.Expected ?? "any value",
        Actual = "not set",
        Remediation = $"Run '{command} config --global {setting.Name} <value>'.",
    };

    private static Finding Ambiguous(string command, VcsSetting setting) => new()
    {
        Message = $"'{setting.Name}' holds more than one value, so which one applies is undefined.",
        Expected = "one value",
        Actual = "several values",
        Remediation =
            $"List them with '{command} config --get-all {setting.Name}' and remove the ones that do not belong.",
    };

    /// <remarks>
    /// Named for the client rather than for the rule. A machine without the
    /// client installed is a fact about the machine, and reporting it as the
    /// rule breaking would send the reader looking for a defect in the tool.
    /// </remarks>
    private static Finding Unavailable(string command, VcsSetting setting) => new()
    {
        Message = $"'{command}' could not be run, so '{setting.Name}' could not be checked.",
        Expected = $"'{command}' on the path",
        Actual = "no such command",
        Remediation = $"Install '{command}', or declare the client this workspace uses under 'vcs'.",
    };

    private static Finding NotARepository(string command) => new()
    {
        Message = $"This workspace declares version control requirements but is not a '{command}' checkout.",
        Expected = $"a '{command}' repository",
        Actual = "no repository here",
        Remediation =
            $"Validate a real checkout, or take 'vcs' out of the workspace manifest if '{command}' is not used.",
    };
}
