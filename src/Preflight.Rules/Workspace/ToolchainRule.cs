namespace Preflight.Rules;

using System.Text.Json;
using Preflight.Abstractions;

/// <summary>
/// Checks that every tool the workspace declares is installed and at an
/// accepted version.
/// </summary>
/// <remarks>
/// The root of the workspace stage and, through <c>gating: true</c>, the rule
/// whose failure makes everything downstream pointless: nothing else can be
/// true about a build if the compiler that would produce it is not there.
/// </remarks>
public sealed class ToolchainRule : IValidationRule
{
    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.Toolchain,
        DisplayName = "Toolchain",
        Stage = ValidationStage.Workspace,
        DefaultBlocking = true,

        // The only rule of the six where gating is true and
        // means something: with no toolchain, running anything after it is
        // spending time to produce noise.
        DefaultGating = true,
    };

    public async Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var manifestPath = Path.Combine(
            context.WorkspaceRoot.FullName,
            context.Policy.GetValue("manifestPath", WorkspaceManifest.DefaultFileName));

        WorkspaceManifest? manifest;

        try
        {
            manifest = await WorkspaceManifest.LoadAsync(context.FileSystem, manifestPath, cancellationToken);
        }
        catch (JsonException exception)
        {
            return RuleOutcome.Failed(new Finding
            {
                Message = "The workspace manifest is not valid JSON.",
                Location = new FindingLocation(manifestPath),
                Actual = exception.Message,
                Remediation = "Fix the syntax, or point 'manifestPath' at the right file in the policy.",
            });
        }

        // A missing manifest fails rather than reporting n/a, and the choice is
        // deliberate. NotApplicable here is a trapdoor: a mistyped
        // 'manifestPath' would make the rule green forever, and a rule that is
        // permanently green is worse than one that is absent, because it is
        // counted as evidence.
        if (manifest is null)
        {
            return RuleOutcome.Failed(new Finding
            {
                Message = "The workspace manifest is missing.",
                Location = new FindingLocation(manifestPath),
                Expected = "a manifest declaring the tools this workspace needs",
                Actual = "no file at that path",
                Remediation =
                    $"Add {WorkspaceManifest.DefaultFileName} at the workspace root, " +
                    "or set 'manifestPath' for this rule in the policy.",
            });
        }

        // A manifest that is present and declares no tools is a different fact:
        // somebody said, in writing, that there is nothing to check.
        if (manifest.Tools.Count == 0)
        {
            return RuleOutcome.NotApplicable();
        }

        var findings = new List<Finding>();

        foreach (var tool in manifest.Tools)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await CheckAsync(context, tool, cancellationToken) is { } finding)
            {
                findings.Add(finding);
            }
        }

        return findings.Count > 0 ? RuleOutcome.Failed([.. findings]) : RuleOutcome.Passed();
    }

    /// <summary>
    /// Reads a version out of whatever the tool printed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The leading numeric run of the first number-looking token on the first
    /// line, capped at four components. Real tools do not print bare versions:
    /// <c>dotnet --version</c> gives <c>10.0.100</c>, a preview install gives
    /// <c>10.0.100-preview.3.25</c>, and <c>git --version</c> on Windows gives
    /// <c>git version 2.51.0.windows.1</c> — five components, the last two of
    /// which are not numbers at all.
    /// </para>
    /// <para>
    /// Taking the leading run rather than the whole token is what makes all
    /// three readable. The alternative, refusing anything that is not exactly a
    /// version, reports a machine that has the tool as having none — and this
    /// was found by a fixture, not by inspection.
    /// </para>
    /// </remarks>
    public static Version? ParseVersion(string output)
    {
        var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();

        if (string.IsNullOrEmpty(firstLine))
        {
            return null;
        }

        var token = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(candidate => char.IsDigit(candidate[0]));

        if (token is null)
        {
            return null;
        }

        // Match, not TryMatch-and-check. The token was selected because its
        // first character is a digit, and the pattern needs exactly one — so a
        // failed match is a state no input can produce, and testing for it
        // would be a branch nothing can take.
        var leading = LeadingVersion.Match(token).Value;

        return Version.TryParse(leading, out var version) ? version : null;
    }

    /// <remarks>
    /// At most four components, because <see cref="Version"/> holds four and a
    /// fifth makes <see cref="Version.TryParse(string, out Version)"/> fail. A
    /// tool that prints five is not describing something this comparison needs
    /// to distinguish.
    /// </remarks>
    private static readonly System.Text.RegularExpressions.Regex LeadingVersion = new(
        @"^\d+(\.\d+){0,3}",
        System.Text.RegularExpressions.RegexOptions.Compiled |
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static async Task<Finding?> CheckAsync(
        RuleContext context,
        ToolRequirement tool,
        CancellationToken cancellationToken)
    {
        ProcessResult result;

        try
        {
            result = await context.Processes.RunAsync(
                new ProcessRequest
                {
                    FileName = tool.Command,
                    Arguments = tool.Arguments,
                    WorkingDirectory = context.WorkspaceRoot.FullName,
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Not swallowed. A timeout is Errored, produced by
            // the engine; a rule that caught its own cancellation would report
            // Failed and blame the workspace for the tool's own deadline.
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Every other way starting a process fails means the tool is not
            // installed, which is exactly what this rule exists to say.
            return Missing(tool, exception.Message);
        }

        if (result.ExitCode != 0)
        {
            return Missing(tool, result.StandardError.Trim());
        }

        var version = ParseVersion(result.StandardOutput);

        if (version is null)
        {
            return new Finding
            {
                Message = $"Could not read a version from '{tool.Name}'.",
                Expected = "a version number on the first line of output",
                Actual = Summarise(result.StandardOutput),
                Remediation = $"Check that '{tool.Command} {string.Join(' ', tool.Arguments)}' prints a version.",
            };
        }

        return OutOfRange(tool, version);
    }

    private static Finding? OutOfRange(ToolRequirement tool, Version version)
    {
        var minimum = Parse(tool.MinimumVersion);
        var maximum = Parse(tool.MaximumVersion);

        // Below the floor, or at or above the ceiling. The ceiling is exclusive
        // because "anything in 10.x" is written 10.0.0 to 11.0.0, and an
        // inclusive one would need a version nobody can write down.
        if ((minimum is null || version >= minimum) && (maximum is null || version < maximum))
        {
            return null;
        }

        return new Finding
        {
            Message = $"'{tool.Name}' is outside the accepted version range.",
            Expected = Describe(minimum, maximum),
            Actual = version.ToString(),
            Remediation = $"Install a '{tool.Name}' inside the accepted range.",
        };
    }

    private static Version? Parse(string? value) =>
        value is not null && Version.TryParse(value, out var version) ? version : null;

    /// <remarks>
    /// Written as nested conditionals rather than a tuple switch because the
    /// switch needs a both-null arm that nothing can reach: with neither bound
    /// set, every version is in range and this is never called. An arm no input
    /// can take is a permanent hole in the branch count, and the usual way that
    /// hole gets closed is a test written to reach it rather than to check
    /// anything.
    /// </remarks>
    private static string Describe(Version? minimum, Version? maximum) =>
        minimum is null
            ? $"below {maximum}"
            : maximum is null
                ? $"at least {minimum}"
                : $"at least {minimum}, below {maximum}";

    private static Finding Missing(ToolRequirement tool, string detail) => new()
    {
        Message = $"'{tool.Name}' is not available.",
        Expected = $"'{tool.Command}' on PATH",
        Actual = detail.Length == 0 ? "the command could not be run" : Summarise(detail),
        Remediation = $"Install '{tool.Name}' and make sure '{tool.Command}' is on PATH.",
    };

    /// <remarks>
    /// Truncated, because this text reaches the console report and, from the
    /// NDJSON history — where a record is capped at 64 KB. A compiler that
    /// prints its whole help on a bad argument would otherwise put all of it in
    /// both.
    /// </remarks>
    private static string Summarise(string text)
    {
        var trimmed = text.Trim();

        return trimmed.Length <= 200 ? trimmed : trimmed[..200] + "…";
    }
}
