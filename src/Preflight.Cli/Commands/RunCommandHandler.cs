namespace Preflight.Cli.Commands;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Cli.Reporting;
using Preflight.Core;

/// <summary>
/// <c>preflight run</c>.
/// </summary>
public static class RunCommandHandler
{
    public static async Task<int> ExecuteAsync(
        CommandEnvironment environment,
        RunOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);

        var descriptors = environment.Rules.Select(rule => rule.Descriptor).ToArray();

        // Nothing in the discovered set answers to the stage
        // the user asked for: that is a question the rule set cannot answer, and
        // no policy decided it. Exit 2, before anything loads.
        if (!descriptors.Any(descriptor => descriptor.Stage == options.Stage))
        {
            throw new RuleDiscoveryException(
                $"No rule has stage '{StageParser.ToArgument(options.Stage)}'. " +
                $"Discovered stages: {string.Join(", ", descriptors.Select(d => StageParser.ToArgument(d.Stage)).Distinct().Order(StringComparer.Ordinal))}.");
        }

        var resolved = await PolicyResolution.ResolveAsync(
            environment.WorkspaceRoot,
            environment.FileSystem,
            environment.Environment,
            descriptors,
            options,
            cancellationToken,
            environment.ResolvedPackage);

        var changed = await ChangedFilesAsync(environment, options, cancellationToken);

        var result = await new RuleExecutor(
                new ConsoleRuleLoggerFactory(environment.Error),
                environment.TimeProvider)
            .ExecuteAsync(
                new RunRequest
                {
                    Rules = environment.Rules,
                    Policy = resolved.Policy,
                    Stage = options.Stage,
                    Target = options.Target.Effective,
                    WorkspaceRoot = environment.WorkspaceRoot,
                    FileSystem = environment.FileSystem,
                    Processes = environment.Processes,
                    ChangedFiles = changed,
                    PolicyChain = resolved.Chain,
                    Pipeline = resolved.Selection.Pipeline,
                    PipelineVersion = resolved.Package?.Version.ToString(),
                    FailOnWarning = options.FailOnWarning,
                    NoSkip = options.NoSkip,
                    RunId = options.RunId,

                    // --no-cache is expressed by there being no cache, not by a
                    // flag the engine has to remember to honour.
                    Cache = options.NoCache ? null : environment.Cache,
                },
                cancellationToken);

        Report(
            environment, options, result, resolved.Overlay, resolved.Selection, resolved.Package, descriptors);

        // After the report, and unable to change it. The history is
        // subordinate to the run: a full partition warns on standard error and
        // the verdict stands.
        await HistoryRecording.RecordAsync(environment, resolved.Policy, result, cancellationToken);

        return ExitCode.ForVerdict(result.Verdict);
    }

    /// <remarks>
    /// Only pre-submit gets a changed-file set, and the parser already refused
    /// <c>--stage pre-submit</c> without a ref, so the only way to reach here
    /// without one is a stage that does not want one.
    /// </remarks>
    private static async Task<IReadOnlyList<ChangedFile>> ChangedFilesAsync(
        CommandEnvironment environment,
        RunOptions options,
        CancellationToken cancellationToken)
    {
        if (options.Stage != ValidationStage.PreSubmit)
        {
            return [];
        }

        return await new GitChangeSource(environment.Processes)
            .GetChangesAsync(environment.WorkspaceRoot, options.ChangedFrom, cancellationToken);
    }

    /// <remarks>
    /// The descriptors travel with the result because the SARIF document needs
    /// <c>DisplayName</c> and <c>Documentation</c>, and both live on
    /// <c>RuleDescriptor</c> rather than on <c>RuleExecution</c>. The caller
    /// already has them, so this is a seam rather than a refactor.
    /// </remarks>
    private static void Report(
        CommandEnvironment environment,
        RunOptions options,
        RunResult result,
        LocalOverlayDecision overlay,
        PipelineSelection selection,
        InstalledPipeline? package,
        IReadOnlyList<RuleDescriptor> descriptors)
    {
        switch (options.Format)
        {
            case ReportFormat.Json:
                new JsonReporter(environment.Console.Output).Report(result);

                return;

            case ReportFormat.Sarif:
                new SarifReporter(environment.Console.Output).Report(result, descriptors);

                return;

            default:
                new ConsoleReporter(
                        environment.Console,
                        GlyphSet.Select(environment.Console, options.NoUnicode))
                    .Report(result, overlay, selection, package);

                return;
        }
    }
}
