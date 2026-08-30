namespace Preflight.Rules;

using System.Text.RegularExpressions;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;

/// <summary>
/// Checks that every tool the workspace declares is installed and at an
/// accepted version.
/// </summary>
/// <remarks>
/// The root of the workspace stage and, through <c>gating: true</c>, the rule
/// whose failure makes everything downstream pointless: nothing else can be
/// true about a build if the compiler that would produce it is not there.
/// </remarks>
public sealed partial class ToolchainRule : IValidationRule
{
    /// <summary>
    /// The leading numeric run of a version-looking token, at most four
    /// components long.
    /// </summary>
    /// <remarks>
    /// Four, because <see cref="Version"/> holds four and a fifth makes
    /// <see cref="Version.TryParse(string, out Version)"/> fail. A tool that
    /// prints five is not describing something this comparison needs to
    /// distinguish.
    ///
    /// Generated at compile time rather than built with
    /// <see cref="RegexOptions.Compiled"/>. That option emits IL on the first
    /// match, and this process lives for seconds against one match per declared
    /// tool — a cost that never amortises. The generator pays it at build time
    /// instead.
    /// </remarks>
    [GeneratedRegex(@"^\d+(\.\d+){0,3}", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingVersion { get; }

    /// <remarks>
    /// Enough for a version banner and the first line of an error, and short
    /// enough that a report listing several missing tools still fits a
    /// terminal. A tool asked for its version answers in one line when it
    /// works; everything longer is it explaining why it did not.
    /// </remarks>
    private const int VersionBannerLimit = 200;

    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.Toolchain,
        DisplayName = "Toolchain",
        Stage = ValidationStage.Workspace,
        DefaultBlocking = true,

        // Gating, and here it decides something: with no compiler installed,
        // nothing downstream can produce a verdict worth reading, so running it
        // spends time to manufacture noise. Everything in the workspace and
        // build stages hangs off this rule for that reason.
        DefaultGating = true,
    };

    public async Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var read = await WorkspaceManifestRead.ReadAsync(context, cancellationToken);

        if (read.Malformed is { } malformed)
        {
            return RuleOutcome.Failed(malformed);
        }

        // A missing manifest fails rather than reporting n/a, and the choice is
        // deliberate. NotApplicable here is a trapdoor: a mistyped
        // 'manifestPath' would make the rule green forever, and a rule that is
        // permanently green is worse than one that is absent, because it is
        // counted as evidence.
        if (read.Manifest is not { } manifest)
        {
            return RuleOutcome.Failed(new Finding
            {
                Message = "The workspace manifest is missing.",
                Location = new FindingLocation(read.ManifestPath),
                Expected = "a manifest declaring the tools this workspace needs",
                Actual = "no file at that path",
                Remediation =
                    $"Add {WorkspaceManifest.DefaultFileName} at the workspace root, " +
                    "or ask the pipeline's author to set 'manifestPath' for this rule.",
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
            // Not swallowed. A timeout is Errored — a defect in the rule or in
            // the environment, and the engine's verdict to give. A rule that
            // caught its own cancellation would report Failed instead, telling
            // the reader the workspace is broken when what happened is that the
            // tool ran past a deadline.
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
                Actual = FindingText.Truncate(result.StandardOutput, VersionBannerLimit),
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
        Actual = detail.Length == 0 ? "the command could not be run" : FindingText.Truncate(detail, VersionBannerLimit),
        Remediation = $"Install '{tool.Name}' and make sure '{tool.Command}' is on PATH.",
    };
}
