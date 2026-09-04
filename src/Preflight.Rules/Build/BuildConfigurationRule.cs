namespace Preflight.Rules;

using System.Text.Json;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Checks that the build configuration for the target is complete and coherent.
/// </summary>
/// <remarks>
/// Complete means every key the production requires is present; coherent means
/// a key that names a directory names one that is there. The second is what
/// stops a configuration from being formally valid and still unbuildable — a
/// build that starts, runs for minutes, and then fails on a missing asset
/// instead of on the wrong path that caused it.
/// </remarks>
public sealed class BuildConfigurationRule : IValidationRule
{
    /// <summary>
    /// Where the configuration lives, with <c>{platform}</c> and
    /// <c>{configuration}</c> replaced from the target.
    /// </summary>
    /// <remarks>
    /// The tokens are what make one rule serve every platform: without them, a
    /// production shipping for three platforms would need three rules or three
    /// policy overlays saying the same thing three ways.
    /// </remarks>
    public const string DefaultPathTemplate = "config/build/{platform}.json";

    public static readonly string[] DefaultRequiredKeys = ["contentRoot"];

    public static readonly string[] DefaultPathKeys = ["contentRoot"];

    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.BuildConfiguration,
        DisplayName = "Build configuration",
        Stage = ValidationStage.BuildReadiness,
        DependsOn = [BuiltInRuleIds.Toolchain],
        DefaultBlocking = true,

        // Gating, because core.build.compile-probe is the
        // expensive rule and probing a build whose configuration is incomplete
        // spends minutes to reproduce something already known.
        DefaultGating = true,
    };

    public async Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var relativePath = Resolve(
            context.Policy.GetValue("path", DefaultPathTemplate),
            context.Target);

        var absolutePath = Path.Combine(context.WorkspaceRoot.FullName, relativePath);

        if (!context.FileSystem.FileExists(absolutePath))
        {
            return RuleOutcome.Failed(new Finding
            {
                Message = "The build configuration for this target is missing.",
                Location = new FindingLocation(relativePath),
                Expected = $"a configuration for {context.Target.Platform}/{context.Target.Configuration}",
                Actual = "no file at that path",
                Remediation =
                    $"Add {relativePath}, or ask the pipeline's author to point 'path' " +
                    "at the right file.",
            });
        }

        JsonElement root;

        try
        {
            using var document = JsonDocument.Parse(
                await context.FileSystem.ReadAllTextAsync(absolutePath, cancellationToken),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            root = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            return RuleOutcome.Failed(new Finding
            {
                Message = "The build configuration is not valid JSON.",
                Location = new FindingLocation(relativePath, LineOf(exception)),
                Actual = exception.Message,
                Remediation =
                    "Fix the syntax, or ask the pipeline's author to point 'path' " +
                    "at the right file.",
            });
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return RuleOutcome.Failed(new Finding
            {
                Message = "The build configuration is not an object.",
                Location = new FindingLocation(relativePath),
                Expected = "a JSON object",
                Actual = root.ValueKind.ToString(),
                Remediation = "Make the configuration a JSON object of build settings.",
            });
        }

        var findings = new List<Finding>();

        Complete(context, root, relativePath, findings, cancellationToken);
        Coherent(context, root, relativePath, findings, cancellationToken);

        return findings.Count > 0 ? RuleOutcome.Failed([.. findings]) : RuleOutcome.Passed();
    }

    /// <summary>
    /// Fills <c>{platform}</c> and <c>{configuration}</c> from the target.
    /// </summary>
    public static string Resolve(string template, BuildTarget target) => template
        .Replace("{platform}", target.Platform, StringComparison.Ordinal)
        .Replace("{configuration}", target.Configuration, StringComparison.Ordinal);

    /// <remarks>
    /// The four strings below are the ones the console, JSON and SARIF worked
    /// examples put in front of a reader — a configuration missing
    /// <c>contentRoot</c> is the finding all three render. Those goldens hold a
    /// hand-written copy of it in the reporter fixture rather than calling this
    /// rule, and nothing links the two: a fixture that built its example by
    /// running a rule would decide what a reporter can be tested with, and the
    /// reporters are meant to work for rules that do not exist yet.
    ///
    /// What does link them is this project's own test, which asserts all four
    /// exactly and is therefore the one that fails when they change. Carry the
    /// change into that copy when it does, or the worked example goes on
    /// quoting a sentence the tool no longer produces.
    /// </remarks>
    private static void Complete(
        RuleContext context,
        JsonElement root,
        string relativePath,
        List<Finding> findings,
        CancellationToken cancellationToken)
    {
        foreach (var key in context.Policy.GetValue("requiredKeys", DefaultRequiredKeys))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!root.TryGetProperty(key, out _))
            {
                findings.Add(new Finding
                {
                    Message = "Missing platform configuration entry.",
                    Location = new FindingLocation(relativePath),
                    Expected = $"a \"{key}\" entry",
                    Actual = "key not present",
                    Remediation =
                        $"Add \"{key}\" to the configuration, or ask the pipeline's author " +
                        "to drop it from 'requiredKeys'.",
                });
            }
        }
    }

    /// <remarks>
    /// The half that makes "coherent" mean something. A configuration naming a
    /// content folder that is not there is formally valid and produces a build
    /// that fails much later, with an error about a missing asset rather than
    /// about a wrong path.
    /// </remarks>
    private static void Coherent(
        RuleContext context,
        JsonElement root,
        string relativePath,
        List<Finding> findings,
        CancellationToken cancellationToken)
    {
        foreach (var key in context.Policy.GetValue("pathKeys", DefaultPathKeys))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var declared = value.GetString();

            if (string.IsNullOrWhiteSpace(declared))
            {
                continue;
            }

            var target = Path.Combine(context.WorkspaceRoot.FullName, declared);

            if (context.FileSystem.DirectoryExists(target) || context.FileSystem.FileExists(target))
            {
                continue;
            }

            findings.Add(new Finding
            {
                Message = $"\"{key}\" points at something that is not there.",
                Location = new FindingLocation(relativePath),
                Expected = $"'{declared}' to exist in the workspace",
                Actual = "nothing at that path",
                Remediation = $"Create '{declared}', or point \"{key}\" at where the content actually is.",
            });
        }
    }

    /// <remarks>
    /// One-based, because that is what every editor shows, and a report
    /// disagreeing with the editor by one is worse than a report carrying no
    /// line at all.
    ///
    /// Excluded from coverage rather than tested.
    /// <see cref="JsonException.LineNumber"/> is declared nullable, but the
    /// parser populates it for every malformed document that can reach here, so
    /// the null arm is unreachable rather than untested. A test written to
    /// reach it would have to fabricate an exception the parser never throws,
    /// which proves nothing about this method and closes the coverage hole with
    /// an assertion about a fiction.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static int? LineOf(JsonException exception) =>
        exception.LineNumber is { } line ? (int)line + 1 : null;
}
