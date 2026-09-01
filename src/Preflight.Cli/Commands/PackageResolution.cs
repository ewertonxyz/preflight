namespace Preflight.Cli.Commands;

using Preflight.Cli.Pipelines;
using Preflight.Cli.Policy;

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
/// options. Those are two different questions. It draws a graph derived from
/// the rule descriptors rather than from any policy, so a flag that selected a
/// policy would promise it changed the picture; but the package contributes
/// rules, and a graph missing them is not the diffable picture of a run.
/// </para>
/// </remarks>
public static class PackageResolution
{
    /// <summary>
    /// The package this invocation uses, or <see langword="null"/> when none
    /// takes part.
    /// </summary>
    /// <param name="environment">The machine, the workspace and the selection.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static InstalledPipeline? For(
        CommandEnvironment environment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);

        cancellationToken.ThrowIfCancellationRequested();

        var selection = environment.Selection;

        if (selection.Pipeline is not { } name)
        {
            return null;
        }

        // True whatever the file says, because a pipeline is in play: the flag
        // names one, or the checkout does. The range still bounds it either way,
        // and refusing here because the base file happens not to repeat the name
        // would refuse a run the flag fully specified.
        var requirement = PipelineSelector.RequirementOf(
            environment.Checkout, pipelineDeclared: true);

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
