namespace Preflight.Rules;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Fails when a variable the workspace declares it needs is not set on this
/// machine.
/// </summary>
/// <remarks>
/// <para>
/// It catches the forty-minute job that dies at the signing step because a
/// secret was never wired into the runner, and the ten-minute one that dies
/// because a toolkit path does not exist on the machine of somebody who joined
/// last week.
/// </para>
/// <para>
/// The manifest can declare a name and nothing else — no pattern, no expected
/// shape — and there is no code path here that can print a value. That is the
/// guarantee, and it is structural rather than a comment somebody has to keep
/// obeying: to explain why a value did not match, the rule would have to quote
/// it, and every field of a finding reaches a build log far more people read
/// than ran the build. The rule never holds a value except to ask whether it is
/// empty.
/// </para>
/// <para>
/// The only rule of its stage that does not hang off the toolchain rule. It
/// starts no process. It reads the manifest to learn <em>what</em> to check
/// rather than whether it may run at all, and a missing compiler makes the
/// answer about a missing variable neither wrong nor unreachable — hanging it
/// there would report an unset secret as "skipped because the compiler is not
/// installed", two independent causes served as one.
/// </para>
/// </remarks>
public sealed class EnvironmentRule : IValidationRule
{
    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.Environment,
        DisplayName = "Environment",
        Stage = ValidationStage.Workspace,
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

        if (context.Environment is not { } probe)
        {
            // Checked before the manifest is even opened, so that a host
            // offering no probe is one answer and not one per declared name.
            return RuleOutcome.NotApplicable();
        }

        var read = await WorkspaceManifestRead.ReadAsync(context, cancellationToken);

        if (read.Malformed is { } malformed)
        {
            return RuleOutcome.Failed(malformed);
        }

        if (read.Manifest is not { Environment.Count: > 0 } manifest)
        {
            return RuleOutcome.NotApplicable();
        }

        var findings = new List<Finding>();

        // By declared entry and in manifest order, duplicates included. The
        // manifest is what the reader is looking at, so a name written twice is
        // answered twice rather than silently collapsed into one line that
        // matches neither of the two the reader can see.
        for (var index = 0; index < manifest.Environment.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = manifest.Environment[index];

            if (string.IsNullOrWhiteSpace(name))
            {
                // Against the manifest, never skipped. A verification switched
                // off by a typo, reported green, is exactly the failure this
                // whole rule exists to prevent one layer up.
                findings.Add(Unnamed(index));

                continue;
            }

            if (Describe(name, probe.Read(name)) is { } finding)
            {
                findings.Add(finding);
            }
        }

        return findings.Count > 0 ? RuleOutcome.Failed([.. findings]) : RuleOutcome.Passed();
    }

    /// <remarks>
    /// Two ways of being unset, told apart, because the remedies differ: a
    /// variable nobody defined is a line missing from the runner's
    /// configuration, and one that arrived empty is a secret that exists and
    /// resolved to nothing — usually a fork build, where secrets are withheld.
    /// Neither string carries the value, and there is no branch here that could
    /// hold one.
    /// </remarks>
    private static Finding? Describe(string name, string? value) => value switch
    {
        null => Unset(name, "not set"),
        _ when value.Trim().Length == 0 => Unset(name, "set, but empty"),
        _ => null,
    };

    private static Finding Unset(string name, string actual) => new()
    {
        Message = $"'{name}' is not set on this machine.",
        Expected = "set to a non-empty value",
        Actual = actual,
        Remediation =
            $"Set '{name}' in this machine's environment, or in the pipeline's secrets, " +
            "before running the build.",
    };

    private static Finding Unnamed(int index) => new()
    {
        Message = $"Entry {index} of 'environment' names no variable.",
        Expected = "a variable name at every position",
        Actual = "an empty entry",
        Remediation = "Correct the 'environment' list in the workspace manifest.",
    };
}
