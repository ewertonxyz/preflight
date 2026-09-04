namespace Preflight.Cli.Commands;

using Preflight.Cli.Model;
using Preflight.Cli.Policy;
using Preflight.Core;
using Preflight.Core.Execution;
using Preflight.Core.History;

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
            environment.Selection,
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
