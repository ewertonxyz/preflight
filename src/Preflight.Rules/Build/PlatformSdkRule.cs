namespace Preflight.Rules;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;

/// <summary>
/// Fails when the platform SDK the policy asks for is missing, or is at a
/// version outside the range the policy accepts.
/// </summary>
/// <remarks>
/// <para>
/// The toolchain rule is blind to the build target: it validates the same list
/// of tools whether the build is for a PC or for a devkit. This one reads the
/// settings the target layer has already specialised, so that a pipeline can
/// require one SDK for one platform and a different one for another without
/// either rule knowing that targets exist.
/// </para>
/// <para>
/// It depends on the toolchain rule and not on the build configuration rule,
/// although both sit in the same stage. What it needs established is that a
/// process can be started at all, which is exactly what the toolchain rule
/// establishes; hanging it off the configuration would let a missing
/// configuration file hide a missing SDK, reporting two independent facts as
/// one.
/// </para>
/// <para>
/// The keys are read flat — <c>sdk.command</c> rather than an <c>sdk</c> object
/// — because the reader a rule is handed resolves leaf values. An object would
/// read exactly as an absent key, which would leave the rule reporting that it
/// checked nothing, forever, with no message to say why.
/// </para>
/// </remarks>
public sealed class PlatformSdkRule : IValidationRule
{
    /// <remarks>
    /// Enough for a version banner and the first line of an error, matching
    /// what the toolchain rule keeps of the same kind of output. Two rules
    /// showing a tool's output at different lengths would read as one of them
    /// being broken.
    /// </remarks>
    private const int VersionBannerLimit = 200;

    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.PlatformSdk,
        DisplayName = "Platform SDK",
        Stage = ValidationStage.BuildReadiness,
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

        cancellationToken.ThrowIfCancellationRequested();

        // The command, and only the command, decides whether this rule has
        // anything to do. A name or a version bound without one describes an SDK
        // nobody said how to find.
        if (!context.Policy.TryGetValue<string>("sdk.command", out var command)
            || string.IsNullOrWhiteSpace(command))
        {
            return RuleOutcome.NotApplicable();
        }

        var name = context.Policy.GetValue("sdk.name", command);
        var platform = context.Target.Platform;

        var probe = await ToolProbe.RunAsync(
            context.Processes,
            new ProcessRequest
            {
                FileName = command,
                Arguments = context.Policy.GetValue("sdk.arguments", Array.Empty<string>()),
                WorkingDirectory = context.WorkspaceRoot.FullName,
            },
            cancellationToken);

        return probe.Status switch
        {
            ToolProbeStatus.Unavailable => RuleOutcome.Failed(Missing(name, command, platform, probe.Detail)),
            ToolProbeStatus.Unreadable => RuleOutcome.Failed(Unreadable(name, command, platform, probe.Detail)),

            // Found, and the version is non-null on that arm by construction of
            // the result. The discard carries it rather than a fourth arm no
            // input could take.
            _ => Judge(context, name, platform, probe.Version!),
        };
    }

    private static RuleOutcome Judge(RuleContext context, string name, string platform, Version version)
    {
        var minimum = Bound(context, "sdk.minimumVersion");
        var maximum = Bound(context, "sdk.maximumVersion");

        // Below the floor, or at or above the ceiling. The ceiling is exclusive
        // because "anything in 10.x" is written 10.0.0 to 11.0.0.
        if ((minimum is null || version >= minimum) && (maximum is null || version < maximum))
        {
            return RuleOutcome.Passed();
        }

        return RuleOutcome.Failed(new Finding
        {
            Message = $"The '{name}' SDK for '{platform}' is outside the accepted version range.",
            Expected = Describe(minimum, maximum),
            Actual = version.ToString(),
            Remediation =
                $"Install '{name}' at a version inside the accepted range, or ask the pipeline's " +
                $"author to adjust the version bounds for '{platform}'.",
        });
    }

    /// <remarks>
    /// A bound that will not parse is ignored rather than obeyed. Read as zero
    /// the rule would accept everything and be silently toothless; read as
    /// infinity it could never be satisfied. Ignoring it leaves the other bound
    /// working, which is the closest thing to what the author meant.
    /// </remarks>
    private static Version? Bound(RuleContext context, string key) =>
        context.Policy.TryGetValue<string>(key, out var value) && Version.TryParse(value, out var version)
            ? version
            : null;

    /// <remarks>
    /// Nested conditionals rather than a tuple switch, because the switch needs
    /// a both-null arm nothing can reach: with neither bound set every version
    /// is in range and this is never called.
    /// </remarks>
    private static string Describe(Version? minimum, Version? maximum) =>
        minimum is null
            ? $"below {maximum}"
            : maximum is null
                ? $"at least {minimum}"
                : $"at least {minimum}, below {maximum}";

    private static Finding Missing(string name, string command, string platform, string detail) => new()
    {
        Message = $"The '{name}' SDK for '{platform}' is not available.",
        Expected = $"'{command}' on PATH",
        Actual = detail.Length == 0 ? "the command could not be run" : FindingText.Truncate(detail, VersionBannerLimit),
        Remediation = $"Install the '{name}' SDK for '{platform}' and make sure '{command}' is on PATH.",
    };

    private static Finding Unreadable(string name, string command, string platform, string output) => new()
    {
        Message = $"Could not read a version from the '{name}' SDK for '{platform}'.",
        Expected = "a version number on the first line of output",
        Actual = FindingText.Truncate(output, VersionBannerLimit),
        Remediation = $"Check that '{command}' prints a version, or point 'sdk.command' at something that does.",
    };
}
