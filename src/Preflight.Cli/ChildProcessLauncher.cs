namespace Preflight.Cli;

using System.Diagnostics;
using Preflight.Core;

/// <summary>
/// Runs a real child process and copies its two streams through, unchanged.
/// </summary>
/// <remarks>
/// <para>
/// Both copies start before the wait. A child that fills a pipe buffer blocks
/// on the write while the parent blocks on the exit, and the measurement
/// deadlocks — which for a build is any real error list.
/// </para>
/// <para>
/// <see cref="Stream.CopyToAsync(Stream, CancellationToken)"/> and not a
/// <see cref="StreamReader"/>: decoding the child's bytes and re-encoding them
/// on the way out would make a wrapper that changes what the wrapped command
/// prints, on any machine whose console encoding differs from the child's. A
/// measurement that alters what it measures is useless, and the same sentence
/// covers the output.
/// </para>
/// </remarks>
public sealed class ChildProcessLauncher : IChildProcessLauncher
{
    /// <inheritdoc />
    public async Task<int> RunAsync(
        ChildProcessRequest request,
        Stream standardOutput,
        Stream standardError,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        var startInfo = new ProcessStartInfo(request.FileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = request.WorkingDirectory,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Start(startInfo);

        var copyingOutput = process.StandardOutput.BaseStream.CopyToAsync(standardOutput, cancellationToken);
        var copyingError = process.StandardError.BaseStream.CopyToAsync(standardError, cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        await copyingOutput;
        await copyingError;

        await standardOutput.FlushAsync(cancellationToken);
        await standardError.FlushAsync(cancellationToken);

        return process.ExitCode;
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
            // with a name, and it becomes 127 rather than a Win32
            // error code nobody can act on.
            throw new ProcessLaunchException(
                $"'{startInfo.FileName}' could not be started: {exception.Message}");
        }
    }

    /// <summary>
    /// Starts the process, refusing a null handle loudly.
    /// </summary>
    /// <remarks>
    /// Extracted purely so its branch can be excluded without taking the Win32
    /// path above with it, exactly as <c>ProcessRunner.Launch</c> is and for
    /// the same reason: <see cref="Process.Start(ProcessStartInfo)"/> returns
    /// null only when the operating system reused an already-running process,
    /// which requires shell execution — disabled above. The throw stays rather
    /// than a null-forgiving operator, because the alternative is a
    /// <c>NullReferenceException</c> three lines later with nothing naming the
    /// executable.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static Process Launch(ProcessStartInfo startInfo) =>
        Process.Start(startInfo)
            ?? throw new ProcessLaunchException($"'{startInfo.FileName}' did not start.");
}
