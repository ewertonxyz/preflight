namespace Preflight.Cli.Commands;

using Preflight.Core;
using Preflight.Core.History;

/// <summary>
/// Everything <c>measure</c> was given.
/// </summary>
/// <param name="Label">The <c>--label</c> the measurement is filed under.</param>
/// <param name="FileName">The child executable.</param>
/// <param name="Arguments">Its arguments, exactly as typed after the <c>--</c>.</param>
/// <param name="Policy">
/// The policy options, because <c>historyPath</c> and <c>historyMode</c> are
/// policy keys, and this command resolves the same chain a run would.
/// </param>
public sealed record MeasureOptions(
    string Label,
    string FileName,
    IReadOnlyList<string> Arguments,
    RunOptions Policy);

/// <summary>
/// <c>preflight measure</c>: the honest comparison.
/// </summary>
/// <remarks>
/// The command exists so a build's duration enters the history <b>measured</b>
/// rather than stated. Everything about it is therefore in service of changing
/// nothing about the command it wraps — the bytes, the exit code, and the time
/// it takes are all the child's.
/// </remarks>
public static class MeasureCommandHandler
{
    public static async Task<int> ExecuteAsync(
        CommandEnvironment environment,
        MeasureOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);

        // Before the child starts. A policy this command cannot read is exit
        // 2, and it would be dishonest to time a child and then
        // discover there is nowhere to record it.
        var resolved = await PolicyResolution.ResolveAsync(
            environment.WorkspaceRoot,
            environment.FileSystem,
            environment.Environment,
            [.. environment.Rules.Select(rule => rule.Descriptor)],
            options.Policy,
            cancellationToken,
            environment.ResolvedPackage);

        var request = new ChildProcessRequest(
            options.FileName,
            options.Arguments,
            environment.WorkspaceRoot.FullName);

        var startedAt = environment.TimeProvider.GetUtcNow();
        var startedTimestamp = environment.TimeProvider.GetTimestamp();

        int exitCode;

        try
        {
            exitCode = await environment.Children.RunAsync(
                request,
                environment.RawOutput,
                environment.RawError,
                cancellationToken);
        }
        catch (ProcessLaunchException exception)
        {
            // 127, not 2: 2 says the invocation of preflight is wrong,
            // and 127 says the command you asked it to measure does not exist.
            // Collapsing them calls the tool's owner about somebody's typo.
            environment.Error.WriteLine(exception.Message);

            return ExitCode.ChildNotStarted;
        }

        await HistoryRecording.RecordAsync(
            environment,
            resolved.Policy,
            new ExternalMeasurement(
                options.Label,
                startedAt,
                environment.TimeProvider.GetElapsedTime(startedTimestamp),
                exitCode,
                request.Describe()),
            cancellationToken);

        return exitCode;
    }
}
