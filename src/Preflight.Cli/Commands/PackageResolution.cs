namespace Preflight.Cli.Commands;

/// <summary>
/// Resolves the installed package for one invocation, once.
/// </summary>
/// <remarks>
/// <para>
/// It runs at the single dispatch point, beside plugin composition, and its
/// answer is handed to both consumers. The alternative — resolving inside the
/// policy chain — would put the package out of reach of <c>graph</c>, which
/// resolves no policy and yet must see every rule the run would execute. A graph
/// missing the package's rules is not the diffable picture of a run that it
/// exists to be.
/// </para>
/// <para>
/// So <c>graph</c> does reach the install root, and still takes no policy
/// options. Those are two different questions and only the second is what
/// <c>Docs/design.md 13</c> denies it. See the phase 10 plan, section 5.0.1.
/// </para>
/// </remarks>
public static class PackageResolution
{
    /// <summary>
    /// The package this invocation uses, or <see langword="null"/> when none
    /// takes part.
    /// </summary>
    /// <param name="environment">The machine and the workspace.</param>
    /// <param name="explicitPipeline">The <c>--pipeline</c> value, if any.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static InstalledPipeline? For(
        CommandEnvironment environment, string? explicitPipeline, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var selection = PipelineSelector.Select(
            environment.WorkspaceRoot, environment.FileSystem, explicitPipeline, cancellationToken);

        if (selection.Pipeline is not { } name)
        {
            return null;
        }

        var requirement = PipelineSelector.RequirementOf(
            environment.WorkspaceRoot, environment.FileSystem, pipelineDeclared: true);

        var workspacePolicy = Path.Combine(
            environment.WorkspaceRoot.FullName, PolicyResolution.PipelineFileName(name));

        return PipelineVersionResolver.Resolve(
            environment.InstallRoot,
            environment.InstalledPipelines,
            environment.MachineState,
            selection,
            requirement,
            environment.FileSystem.FileExists(workspacePolicy));
    }
}
