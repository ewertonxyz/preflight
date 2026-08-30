namespace Preflight.Rules;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Checks that every declared dependency can be satisfied, and that it has
/// been.
/// </summary>
/// <remarks>
/// <para>
/// The rule that distinguishes two degrees of problem, and the distinction is
/// why <c>RuleStatus.Warning</c> is a status the tool actually produces rather
/// than a value only the enum has: "run a restore" and "this version cannot be
/// had" are different sentences for whoever reads the report, and only the
/// first is fixed by a command nobody has to think about.
/// </para>
/// <para>
/// Both checks are offline. The tool never fetches anything, so "resolvable"
/// cannot mean "a feed has it" — that would be a network call, and a validation
/// run that behaves differently on a train is not a validation run. It means
/// the declaration itself can be satisfied by any restore: a dependency with no
/// version cannot, and neither can two declarations of one id at different
/// versions.
/// </para>
/// </remarks>
public sealed class DependenciesRule : IValidationRule
{
    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.Dependencies,
        DisplayName = "Dependencies",
        Stage = ValidationStage.Workspace,
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

        if (read.Manifest is not { Dependencies.Count: > 0 } manifest)
        {
            // Unlike the toolchain rule, a missing manifest here is not a
            // trapdoor: this rule does not run alone. It depends on
            // core.workspace.toolchain, which fails loudly on the same missing
            // file, so reporting it twice would put the same problem on two
            // lines and make the summary count disagree with the number of
            // things to fix.
            return RuleOutcome.NotApplicable();
        }

        var unsatisfiable = Unsatisfiable(manifest.Dependencies);

        if (unsatisfiable.Count > 0)
        {
            return RuleOutcome.Failed([.. unsatisfiable]);
        }

        var unrestored = Unrestored(context, manifest.Dependencies, cancellationToken);

        return unrestored.Count > 0 ? RuleOutcome.Warned([.. unrestored]) : RuleOutcome.Passed();
    }

    /// <summary>
    /// Declarations that no restore could satisfy. The <c>Failed</c> arm.
    /// </summary>
    private static List<Finding> Unsatisfiable(IReadOnlyList<DependencyRequirement> dependencies)
    {
        var findings = new List<Finding>();

        foreach (var dependency in dependencies.Where(dependency => string.IsNullOrWhiteSpace(dependency.Version)))
        {
            findings.Add(new Finding
            {
                Message = $"'{dependency.Id}' is declared without a version.",
                Expected = "a version for every declared dependency",
                Actual = "no version",
                Remediation = $"Declare the version '{dependency.Id}' should resolve to.",
            });
        }

        // Two declarations of one id at different versions. A restore has to
        // pick one, and which one it picks is the kind of thing that differs
        // between a developer's machine and the build agent — the exact failure
        // this tool exists to catch before the build does.
        var conflicts = dependencies
            .Where(dependency => !string.IsNullOrWhiteSpace(dependency.Version))
            .GroupBy(dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(dependency => dependency.Version)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1);

        foreach (var conflict in conflicts)
        {
            var versions = conflict
                .Select(dependency => dependency.Version)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal);

            findings.Add(new Finding
            {
                Message = $"'{conflict.Key}' is declared at more than one version.",
                Expected = "one version per dependency",
                Actual = string.Join(", ", versions),
                Remediation = $"Reconcile the declarations of '{conflict.Key}' to a single version.",
            });
        }

        return findings;
    }

    /// <summary>
    /// Declarations that are fine but have not been restored. The
    /// <c>Warning</c> arm.
    /// </summary>
    /// <remarks>
    /// A dependency with no marker is not reported. The manifest is saying it
    /// has no way to tell, and inventing a verdict from that would be the rule
    /// asserting something nobody told it.
    /// </remarks>
    private static List<Finding> Unrestored(
        RuleContext context,
        IReadOnlyList<DependencyRequirement> dependencies,
        CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();

        foreach (var dependency in dependencies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(dependency.RestoredMarker))
            {
                continue;
            }

            var marker = Path.Combine(context.WorkspaceRoot.FullName, dependency.RestoredMarker);

            if (context.FileSystem.FileExists(marker) || context.FileSystem.DirectoryExists(marker))
            {
                continue;
            }

            findings.Add(new Finding
            {
                Message = $"'{dependency.Id}' is declared but not restored.",
                Location = new FindingLocation(dependency.RestoredMarker),
                Expected = "the dependency restored into the workspace",
                Actual = "nothing at the restored marker",

                // The remedy is a command, which is exactly what makes this a
                // warning rather than a failure: nobody has to decide anything.
                Remediation = "Run the workspace's restore step and try again.",
            });
        }

        return findings;
    }
}
