namespace Preflight.Core.Execution;

using System.Diagnostics;
using Preflight.Abstractions.Services;

/// <summary>
/// Runs a real child process.
/// </summary>
/// <remarks>
/// <para>
/// Lives here rather than in <c>Preflight.Cli</c> because the integration tests
/// run the built-in rules against real fixtures from
/// <c>Preflight.Rules.Tests</c>, and a test project cannot reference an
/// executable.
/// </para>
/// <para>
/// Arguments are passed through <see cref="ProcessStartInfo.ArgumentList"/> and
/// never as a joined string. The joined form makes quoting the caller's
/// problem, and the caller here is <c>--changed-from</c>, which is whatever the
/// user typed.
/// </para>
/// </remarks>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo(request.FileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (request.WorkingDirectory is { } workingDirectory)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var startedAt = Stopwatch.GetTimestamp();

        using var process = Start(startInfo);

        // Both streams are read before the wait. A child that fills a pipe
        // buffer blocks on the write while the parent blocks on the exit, and
        // the run deadlocks — a failure that only shows up once the output grows
        // past the buffer, which for a compiler probe is any real error list.
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A cancelled run kills its children. Leaving a
            // compiler running after the run gave up is how a build machine
            // accumulates processes nobody can attribute to anything.
            Kill(process);

            throw;
        }

        return new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError,
            Stopwatch.GetElapsedTime(startedAt));
    }

    private static Process Start(ProcessStartInfo startInfo)
    {
        try
        {
            return Launch(startInfo);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            // The executable is not on PATH. That is a configuration problem
            // with a name — "git is not installed" — and it deserves that
            // sentence rather than a Win32 error code.
            throw new ProcessLaunchException(
                $"'{startInfo.FileName}' could not be started: {exception.Message}");
        }
    }

    /// <summary>
    /// Starts the process, refusing a null handle loudly.
    /// </summary>
    /// <remarks>
    /// Extracted purely so its branch can be excluded without taking the Win32
    /// path above with it. <see cref="Process.Start(ProcessStartInfo)"/>
    /// returns null only when the operating system reused an already-running
    /// process for the request, which requires shell execution — disabled here.
    /// The throw stays rather than a null-forgiving operator, because the
    /// alternative is a NullReferenceException three lines later with nothing
    /// naming the executable.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static Process Launch(ProcessStartInfo startInfo) =>
        Process.Start(startInfo)
            ?? throw new ProcessLaunchException($"'{startInfo.FileName}' did not start.");

    /// <summary>
    /// Kills the child and its tree, tolerating one having already exited.
    /// </summary>
    /// <remarks>
    /// The catch is a real race and not a defensive habit: cancellation and the
    /// child's own exit can land in either order, and losing that race means
    /// the desired state was reached by other means. It is excluded rather than
    /// covered because provoking it reliably would mean timing the two against
    /// each other, and a test that has to win a race to pass is a test that
    /// fails on a loaded machine — the sort that gets deleted rather than
    /// fixed.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
