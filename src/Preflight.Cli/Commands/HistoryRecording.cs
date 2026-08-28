namespace Preflight.Cli.Commands;

using Preflight.Core;
using Preflight.Core.History;
using Preflight.Core.Policy;

/// <summary>
/// Writes one history event, and refuses to let failing at it change the
/// outcome of the command it belongs to.
/// </summary>
/// <remarks>
/// <para>
/// A failure to write the history does <b>not</b> alter the verdict or the exit
/// code. Instrumentation is subordinate to the function it instruments, and a
/// full partition must not turn a <c>Passed</c> into an error. It warns on
/// standard error and carries on.
/// </para>
/// <para>
/// The two catch clauses are the two ways a disk says no, named rather than
/// swallowed wholesale: a general catch here would also hide a defect in the
/// serialiser, which is not a disk problem and should not be reported as one.
/// </para>
/// </remarks>
public static class HistoryRecording
{
    /// <summary>
    /// Records a run.
    /// </summary>
    public static Task RecordAsync(
        CommandEnvironment environment,
        EffectivePolicy policy,
        RunResult result,
        CancellationToken cancellationToken) =>
        AttemptAsync(
            environment,
            policy,
            (writer, settings) => writer.WriteRunAsync(environment.WorkspaceRoot, settings, result, cancellationToken));

    /// <summary>
    /// Records a measured child process.
    /// </summary>
    public static Task RecordAsync(
        CommandEnvironment environment,
        EffectivePolicy policy,
        ExternalMeasurement measurement,
        CancellationToken cancellationToken) =>
        AttemptAsync(
            environment,
            policy,
            (writer, settings) => writer.WriteExternalAsync(
                environment.WorkspaceRoot,
                settings,
                measurement,
                cancellationToken));

    private static async Task AttemptAsync(
        CommandEnvironment environment,
        EffectivePolicy policy,
        Func<NdjsonHistoryWriter, HistorySettings, Task> write)
    {
        var settings = HistorySettings.From(policy);

        try
        {
            await write(
                new NdjsonHistoryWriter(environment.History, environment.Machine, environment.TimeProvider),
                settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            environment.Error.WriteLine(
                $"preflight: the history at '{settings.Path}' was not written: {exception.Message}");
        }
    }
}
