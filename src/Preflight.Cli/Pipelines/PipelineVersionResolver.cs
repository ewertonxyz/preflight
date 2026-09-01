namespace Preflight.Cli.Pipelines;

using Preflight.Cli.Policy;
using Preflight.Cli.Services;
using Preflight.Core.Policy;

/// <summary>
/// Decides which installed package version serves this run, or refuses.
/// </summary>
/// <remarks>
/// <para>
/// The whole of the phase's decision, in one place, and pure: every fact it
/// needs arrives as a parameter, so the eight rows of the matrix are testable
/// without a disk, an environment or a clock.
/// </para>
/// <para>
/// The matrix, weakest claim first. A workspace policy file wins when the
/// checkout states no requirement — that is the world every fixture and every
/// installation from phases 1 to 9 lives in, and taking it away on the day of an
/// upgrade would be a migration wearing the clothes of a feature. A checkout
/// that states a requirement <em>and</em> carries its own copy is refused rather
/// than resolved, because picking a winner hides the contradiction in the one
/// file that was supposed to settle it.
/// </para>
/// <para>
/// Every failure here is exit 2, and every one of them names the way out. The
/// refusal that matters most is a pin outside the range when a satisfying
/// version is installed: switching would be convenient and would silently
/// discard the one value somebody wrote by hand. It is the same inference the
/// pipeline name refuses, one storey up: a single plausible answer is still not
/// an answer anybody gave.
/// </para>
/// </remarks>
public static class PipelineVersionResolver
{
    /// <summary>
    /// Resolves, or throws a configuration error.
    /// </summary>
    /// <param name="root">Where packages are installed.</param>
    /// <param name="installed">What is installed.</param>
    /// <param name="state">The pins.</param>
    /// <param name="selection">Which pipeline this run uses, and who decided.</param>
    /// <param name="requirement">The range the checkout accepts, if it states one.</param>
    /// <param name="workspacePolicyExists">
    /// Whether <c>preflight.&lt;name&gt;.json</c> sits in the workspace.
    /// </param>
    /// <returns>
    /// The package to use, or <see langword="null"/> when no package takes part —
    /// which is both the pre-package world and a workspace with nothing selected.
    /// </returns>
    /// <exception cref="PolicyValidationException">The state is contradictory or unsatisfiable.</exception>
    public static InstalledPipeline? Resolve(
        PipelineInstallRoot root,
        IInstalledPipelineReader installed,
        MachineState state,
        PipelineSelection selection,
        PipelineRequirement? requirement,
        bool workspacePolicyExists)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.Pipeline is not { } name)
        {
            return null;
        }

        if (workspacePolicyExists)
        {
            // A checkout that both requires a package and carries its own copy
            // is contradicting itself in the one file that was meant to settle
            // the question. Picking a winner would hide that, and the losing
            // half would go on being edited by somebody who believed it counted.
            if (requirement is not null)
            {
                throw Refusal(
                    $"This workspace holds '{PolicyResolution.PipelineFileName(name)}' and also " +
                    $"declares '{PipelineRequirement.KeyName}' in {PolicyResolution.BaseFileName}. " +
                    "Keep one: either the pipeline is delivered as a package, or it lives here.");
            }

            return null;
        }

        var versions = installed.Versions(name);
        var pinned = state.Pins.TryGetValue(name, out var pin) ? pin : null;

        if (pinned is not null)
        {
            return Pinned(root, name, pinned, requirement, versions);
        }

        var eligible = requirement is null
            ? versions
            : [.. versions.Where(version => version.Satisfies(requirement))];

        if (eligible.Count == 0)
        {
            return requirement is null
                ? null
                : throw Refusal(Missing(name, requirement, versions));
        }

        var newest = eligible[^1];

        return new InstalledPipeline(
            name,
            newest,
            root.VersionDirectory(name, newest),
            requirement is null ? PipelineVersionSource.Newest : PipelineVersionSource.Requirement);
    }

    /// <remarks>
    /// The pin decides, or the run stops. Quietly moving to a version that does
    /// satisfy the range would discard the one value in this decision that
    /// somebody wrote by hand, and would do it on a machine where nobody is
    /// looking — the same inference the pipeline name refuses, one storey up.
    /// A pin whose directory is gone is the same refusal for the same
    /// reason: falling through to the newest installed version is how a run
    /// validates against limits nobody chose.
    /// </remarks>
    private static InstalledPipeline Pinned(
        PipelineInstallRoot root,
        string name,
        PackageVersion pinned,
        PipelineRequirement? requirement,
        IReadOnlyList<PackageVersion> versions)
    {
        if (!versions.Contains(pinned))
        {
            throw Refusal(
                $"'{name}' is pinned to {pinned}, which is not installed. " +
                $"Install it, or choose another with 'preflight pipeline use {name}@<version>'.");
        }

        if (requirement is not null && !pinned.Satisfies(requirement))
        {
            var satisfying = versions.Where(version => version.Satisfies(requirement)).ToArray();

            var remedy = satisfying.Length > 0
                ? $"Run 'preflight pipeline use {name}@{satisfying[^1]}'."
                : $"No installed version satisfies it; install one first.";

            throw Refusal(
                $"'{name}' is pinned to {pinned}, and this checkout requires " +
                $"{Describe(requirement)}. {remedy}");
        }

        return new InstalledPipeline(
            name, pinned, root.VersionDirectory(name, pinned), PipelineVersionSource.Pin);
    }

    private static string Missing(
        string name, PipelineRequirement requirement, IReadOnlyList<PackageVersion> versions)
    {
        var present = versions.Count == 0
            ? "nothing is installed"
            : $"installed: {string.Join(", ", versions)}";

        return $"This checkout requires '{name}' {Describe(requirement)}, and {present}. " +
            "Install a version that satisfies it.";
    }

    private static string Describe(PipelineRequirement requirement) =>
        requirement.Maximum is null
            ? $"{requirement.Minimum} or newer"
            : $"at least {requirement.Minimum} and below {requirement.Maximum}";

    private static PolicyValidationException Refusal(string message) =>
        new([new PolicyValidationError(message, null, null, PipelineRequirement.KeyName)]);
}
