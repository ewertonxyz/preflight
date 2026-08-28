namespace Sample.Production.Rules;

using Preflight.Abstractions;

/// <summary>
/// Fails when a changed texture is larger than the production allows.
/// </summary>
/// <remarks>
/// <para>
/// The worked example of a production's own rule, as a project that
/// really builds. It exists to be copied, so everything in it is the shortest
/// honest version of itself rather than the most capable.
/// </para>
/// <para>
/// Three things in it are the rule contract in use rather than matters of
/// style: the file is read through
/// <c>context.FileSystem</c> instead of <c>File.ReadAllBytes</c>, which is what
/// makes the rule unit-testable; the cancellation token is checked inside the
/// loop, because a loop over a thousand textures has to be interruptible; and
/// "no texture among the changed files" is <c>NotApplicable</c>, not
/// <c>Passed</c> — a tick there would claim a check that never happened.
/// </para>
/// <para>
/// Another production sets <c>maxDimension: 8192</c> in its own policy and uses
/// this same DLL. Nothing here knows which production it is running for.
/// </para>
/// </remarks>
public sealed class TextureDimensionRule : IValidationRule
{
    /// <summary>The limit when the policy states none.</summary>
    public const int DefaultMaxDimension = 4096;

    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = new RuleId("atlas.content.texture-dimension"),
        DisplayName = "Texture dimension",
        Stage = ValidationStage.PreSubmit,
        DefaultSeverity = Severity.Error,
        DefaultBlocking = true,

        // Nothing depends on this rule, so gating decides nothing. Stated
        // rather than left to the descriptor's own default, so that a reader
        // copying this file does not read the default as a decision.
        DefaultGating = false,
        Documentation = "https://wiki/atlas/rules/texture-dimension",
    };

    public async Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var maxDimension = context.Policy.GetValue("maxDimension", DefaultMaxDimension);

        var candidates = context.ChangedFiles
            .Where(file => file.Kind != ChangeKind.Deleted)
            .Where(file => TextureProbe.IsTexture(file.RelativePath))
            .ToList();

        if (candidates.Count == 0)
        {
            return RuleOutcome.NotApplicable();
        }

        var findings = new List<Finding>();

        foreach (var file in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The new path, never PreviousRelativePath: a rename's old path
            // names a file that no longer exists. Combined with the workspace
            // root because a changed file is reported relative to it.
            var size = await TextureProbe.TryReadDimensionsAsync(
                context.FileSystem,
                Path.Combine(context.WorkspaceRoot.FullName, file.RelativePath),
                cancellationToken);

            // A texture the probe could not read is not a texture that broke the
            // limit. Reporting one would be a rule failing a commit over its own
            // inability to parse a file.
            if (size is null || (size.Width <= maxDimension && size.Height <= maxDimension))
            {
                continue;
            }

            findings.Add(Describe(file.RelativePath, size, maxDimension));
        }

        return findings.Count == 0
            ? RuleOutcome.Passed()
            : RuleOutcome.Failed([.. findings]);
    }

    private static Finding Describe(string relativePath, TextureSize size, int maxDimension) => new()
    {
        Message = "Texture exceeds the dimension limit for this production.",
        Location = new FindingLocation(relativePath),
        Expected = $"<= {maxDimension}px",
        Actual = $"{size.Width}x{size.Height}px",
        Remediation = "Downscale the source texture or request a policy exception.",
    };
}
