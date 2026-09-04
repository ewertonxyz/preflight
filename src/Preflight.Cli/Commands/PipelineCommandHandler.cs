namespace Preflight.Cli.Commands;

using Preflight.Cli.Interactive;
using Preflight.Cli.Model;
using Preflight.Cli.Pipelines;
using Preflight.Cli.Policy;
using Preflight.Core;

/// <summary>
/// <c>preflight pipeline declare</c>, <c>use</c> and <c>list</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>declare</c> and <c>use</c> answer two different questions and are two
/// commands for that reason. <c>declare</c> writes into the checkout — a
/// versioned file, read by everyone, and it never replaces one. <c>use</c>
/// writes the machine's pin, which exists to be changed and is overwritten every
/// time. One command with two modes would let somebody open it to switch their
/// own version and leave with a change committable to the whole team.
/// </para>
/// <para>
/// The asymmetry is deliberate and is pinned by a test on each side, so that a
/// later refactor which harmonises them breaks loudly.
/// </para>
/// </remarks>
public static class PipelineCommandHandler
{
    /// <summary>
    /// Writes the two checkout keys, or refuses because the file already exists.
    /// </summary>
    /// <remarks>
    /// Creates <c>preflight.base.json</c> and never edits one. The format allows
    /// comments and trailing commas — that is why a policy file can say why a
    /// limit is what it is — and a read-modify-write round trip loses them. A
    /// command that deletes somebody's comment is worse than a command that
    /// declines and says what to add.
    /// </remarks>
    public static async Task<int> DeclareAsync(
        CommandEnvironment environment, string? name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var path = Path.Combine(environment.WorkspaceRoot.FullName, PolicyResolution.BaseFileName);

        // Before the name is resolved, and before anybody is asked anything. A
        // prompt whose answer is thrown away by the next line is a question that
        // wasted somebody's attention.
        if (environment.WorkspaceWriter.Exists(path))
        {
            throw new PipelineCommandException(
                $"'{PolicyResolution.BaseFileName}' already exists at {path}. " +
                "Add a 'pipeline' key to it yourself; this command never edits a file, " +
                "because the format allows comments and rewriting it would drop them.");
        }

        // No name is a question, not a default. What the picker offers is what
        // this machine has installed, and it refuses outright when there is
        // nobody to answer — a checkout key nobody chose would be committed to
        // the whole team's repository.
        name ??= PipelinePicker.Choose(
            environment, SelectionModel.ForPipelines(environment.InstalledPipelines.Pipelines()));

        PipelineName.Require(name);

        var installed = environment.InstalledPipelines.Versions(name);
        var content = Skeleton(name, installed.Count > 0 ? installed[^1] : null);

        try
        {
            await environment.WorkspaceWriter.WriteNewAsync(path, content, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PipelineCommandException(
                $"Could not write '{PolicyResolution.BaseFileName}' at {path}: {exception.Message}");
        }

        environment.Console.Output.WriteLine($"Wrote {PolicyResolution.BaseFileName}.");
        environment.Console.Output.WriteLine($"This checkout is now the '{name}' pipeline.");

        return ExitCode.Success;
    }

    /// <summary>
    /// Pins a version on this machine.
    /// </summary>
    /// <remarks>
    /// A version that is not installed is refused here rather than at the next
    /// run, so the complaint arrives beside the command that caused it instead
    /// of in front of whoever runs <c>preflight run</c> next.
    /// </remarks>
    public static Task<int> UseAsync(
        CommandEnvironment environment, string? argument, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);

        cancellationToken.ThrowIfCancellationRequested();

        var (name, version) = argument is not null && argument.Contains('@', StringComparison.Ordinal)
            ? ParseSelector(argument)
            : ParseSelector(Ask(environment, argument));
        var installed = environment.InstalledPipelines.Versions(name);

        if (!installed.Contains(version))
        {
            var present = installed.Count == 0
                ? "nothing is installed for it"
                : $"installed: {string.Join(", ", installed)}";

            throw new PipelineCommandException(
                $"'{name}@{version}' is not installed, and {present}. " +
                "Install it before pinning it.");
        }

        var pins = new Dictionary<string, PackageVersion>(
            environment.MachineState.Pins, StringComparer.OrdinalIgnoreCase)
        {
            [name] = version,
        };

        environment.MachineStateStore.Write(
            environment.InstallRoot.MachineStatePath,
            environment.MachineState with { Pins = pins });

        environment.Console.Output.WriteLine($"Pinned {name}@{version} on this machine.");

        return Task.FromResult(ExitCode.Success);
    }

    /// <summary>
    /// Prints what is installed, and which version each pipeline would use.
    /// </summary>
    /// <remarks>
    /// The reason column says why, in the same words the resolver uses, because
    /// "which version is active" without "and why" is the half of the answer
    /// that does not help anybody decide what to do next.
    /// </remarks>
    public static Task<int> ListAsync(
        CommandEnvironment environment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);

        cancellationToken.ThrowIfCancellationRequested();

        var pipelines = environment.InstalledPipelines.Pipelines();

        if (pipelines.Count == 0)
        {
            // Empty is an answer, not an error — the same judgement `report`
            // makes about an empty history.
            environment.Console.Output.WriteLine(
                $"No pipelines installed in {environment.InstallRoot.Root.FullName}.");

            return Task.FromResult(ExitCode.Success);
        }

        environment.Console.Output.WriteLine($"Installed in {environment.InstallRoot.Root.FullName}");

        foreach (var pipeline in pipelines)
        {
            var pinned = environment.MachineState.Pins.TryGetValue(pipeline, out var pin) ? pin : null;

            environment.Console.Output.WriteLine($"  {pipeline}");

            foreach (var version in environment.InstalledPipelines.Versions(pipeline))
            {
                var marker = version == pinned ? "*" : " ";
                var reason = version == pinned ? "   pinned" : string.Empty;

                environment.Console.Output.WriteLine($"  {marker} {version}{reason}");
            }

            if (pinned is null)
            {
                environment.Console.Output.WriteLine(
                    "      no pin; a run takes the newest a checkout's requiresPipeline allows");
            }
        }

        return Task.FromResult(ExitCode.Success);
    }

    /// <remarks>
    /// <c>name@version</c>, and nothing else. The separator is <c>@</c> rather
    /// than a space because the pair is one argument in a script, and rather
    /// than <c>:</c> because that is already the separator <c>--set</c> and
    /// <c>sealed</c> use for a different job.
    /// </remarks>
    /// <summary>
    /// Asks which version to pin, when the command line did not say.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pipeline comes from <paramref name="argument"/> when it names one,
    /// and otherwise from the checkout, which is where that question is
    /// answered and where it stays. The picker chooses the <em>version</em>,
    /// and never the pipeline a run validates against: a menu answering that
    /// would let a developer's machine and the build server validate against
    /// different rules, with nothing in either header saying so.
    /// </para>
    /// <para>
    /// The range the checkout accepts decorates the rows and removes none of
    /// them. A version outside it is still installed and still pinnable — that
    /// state is precisely what somebody opens this menu to get out of — and a
    /// list shorter than the disk it describes explains nothing.
    /// </para>
    /// </remarks>
    private static string Ask(CommandEnvironment environment, string? argument)
    {
        // One read of preflight.base.json, answering both questions below.
        // This command manages packages, so the dispatch point resolved no
        // selection for it and there is nothing on the environment to reuse.
        var checkout = CheckoutDocument.Read(environment.WorkspaceRoot, environment.FileSystem);

        var selection = PipelineSelector.Select(
            environment.WorkspaceRoot, environment.FileSystem, argument, checkout, CancellationToken.None);

        if (selection.Pipeline is not { } name)
        {
            throw new PipelineCommandException(
                "Which pipeline? This checkout does not say, so there is nothing to offer a version of. " +
                "Pass 'name@version', or run 'preflight pipeline declare' first.");
        }

        PipelineName.Require(name);

        var requirement = PipelineSelector.RequirementOf(
            checkout, pipelineDeclared: selection.Source is PipelineSource.Checkout);

        environment.MachineState.Pins.TryGetValue(name, out var pinned);

        return PipelinePicker.Choose(
            environment,
            SelectionModel.ForVersions(
                name, environment.InstalledPipelines.Versions(name), pinned, requirement));
    }

    private static (string Name, PackageVersion Version) ParseSelector(string argument)
    {
        var separator = argument.IndexOf('@', StringComparison.Ordinal);

        if (separator <= 0 || separator == argument.Length - 1)
        {
            throw new PipelineCommandException(
                $"'{argument}' is not a pipeline selector. Expected 'name@version', as in 'projecta@1.4.0'.");
        }

        var name = argument[..separator];

        PipelineName.Require(name);

        if (!PackageVersion.TryParse(argument[(separator + 1)..], out var version))
        {
            throw new PipelineCommandException(
                $"'{argument[(separator + 1)..]}' is not a package version. " +
                "Expected three numbers, as in '1.4.0'.");
        }

        return (name, version!);
    }

    /// <remarks>
    /// The requirement is written active when a package of that name is
    /// installed, and commented out when none is. A <c>declare</c> that always
    /// wrote an active range would produce a file whose next run is exit 2 —
    /// the command would break the workspace it had just set up.
    /// </remarks>
    private static string Skeleton(string name, PackageVersion? installed)
    {
        var requirement = installed is null
            ? $$"""
                // No '{{name}}' package is installed on this machine, so the version range
                // is left commented out. Uncomment it once one is, and the CI will then
                // refuse a machine carrying a package this checkout does not accept.
                //
                // "requiresPipeline": {
                //   "minimumVersion": "1.0.0",
                //   "maximumVersion": "2.0.0"
                // }
                """
            : $$"""
                // The range of '{{name}}' package versions this checkout accepts.
                // minimumVersion is inclusive, maximumVersion is exclusive.
                "requiresPipeline": {
                  "minimumVersion": "{{installed}}",
                  "maximumVersion": "{{installed.Major + 1}}.0.0"
                }
                """;

        return $$"""
            {
              "schemaVersion": 1,

              // Which pipeline this checkout is. With this key nobody has to type
              // --pipeline, and a workspace holding several is no longer ambiguous.
              "pipeline": "{{name}}",

              {{requirement}}
            }

            """;
    }
}
