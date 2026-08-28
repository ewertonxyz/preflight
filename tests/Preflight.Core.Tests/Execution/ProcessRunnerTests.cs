namespace Preflight.Core.Tests.Execution;

using Preflight.Abstractions.Services;
using Preflight.Core;

/// <summary>
/// Fixes the real process runner: the parts a substitute cannot stand in for.
/// </summary>
/// <remarks>
/// <para>
/// Every rule test in the project substitutes <see cref="IProcessRunner"/>, so
/// nothing else ever exercises the shipped one. A runner that never started a
/// process, or lost every byte of output, would leave the entire suite green.
/// </para>
/// <para>
/// The sleeping process below is <c>ping</c>, which ties two of these tests to
/// Windows. That matches the rest of the project's verification —
/// <c>scripts/verify.ps1</c> is PowerShell and <c>.gitattributes</c> keeps it
/// CRLF for Windows PowerShell — and it is stated here rather than discovered
/// by whoever first runs the suite elsewhere.
/// </para>
/// </remarks>
public sealed class ProcessRunnerTests
{
    private readonly ProcessRunner _runner = new();

    [Fact]
    public async Task RunAsync_CapturesStandardOutputAndTheExitCode()
    {
        var result = await _runner.RunAsync(
            new ProcessRequest { FileName = "git", Arguments = ["--version"] },
            CancellationToken.None);

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("git version");
        result.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task RunAsync_CapturesStandardErrorAndANonZeroExitCode()
    {
        var result = await _runner.RunAsync(
            new ProcessRequest { FileName = "git", Arguments = ["rev-parse", "--verify", "preflight-no-such-ref"] },
            CancellationToken.None);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RunAsync_RunsInTheRequestedWorkingDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("preflight-runner-");

        try
        {
            var result = await _runner.RunAsync(
                new ProcessRequest
                {
                    FileName = "git",
                    Arguments = ["rev-parse", "--is-inside-work-tree"],
                    WorkingDirectory = directory.FullName,
                },
                CancellationToken.None);

            // Outside a work tree, so git refuses. The assertion is that it
            // refused about *this* directory rather than about the repository
            // the test host happens to be running in.
            result.ExitCode.ShouldNotBe(0);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <remarks>
    /// A missing executable is a configuration problem with a name — "git is
    /// not installed" — not a Win32 error code, and not a defect in the tool.
    /// The exit-code contract makes that distinction decide who gets called.
    /// </remarks>
    [Fact]
    public async Task RunAsync_WithAnExecutableThatDoesNotExist_ThrowsAConfigurationError()
    {
        var exception = await Should.ThrowAsync<ProcessLaunchException>(() =>
            _runner.RunAsync(
                new ProcessRequest { FileName = "preflight-no-such-executable" },
                CancellationToken.None));

        exception.ShouldBeAssignableTo<ConfigurationLoadException>();
        exception.Message.ShouldContain("preflight-no-such-executable");
    }

    /// <summary>
    /// A cancelled run does not leave its child behind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The concurrency contract requires the token to reach every child
    /// process, and rule isolation kills the child of a rule that timed out. A
    /// runner that propagated the cancellation to its own await and left the
    /// process running would pass every other test here while a build machine
    /// accumulated compilers nobody could attribute to anything.
    /// </para>
    /// <para>
    /// It asserts on <em>identities</em> and not on a count, and it waits for
    /// them. The first version compared
    /// <c>Process.GetProcessesByName("PING").Length</c> against a count taken
    /// beforehand, and it flaked — for a reason that had nothing to do with the
    /// runner. Killing a process does not remove it from that list; the
    /// operating system reaps it when it gets round to it, so the count
    /// immediately after a successful kill is still the count before it plus
    /// one. The assertion was measuring the scheduler.
    /// </para>
    /// <para>
    /// Comparing sets of process ids removes the other half of the problem too:
    /// a <c>ping</c> that was already running when the test started is no
    /// longer counted against it. What remains, and is worth stating rather
    /// than hiding, is that a <em>foreign</em> ping started inside the window
    /// would be read as a survivor. That is a much smaller window than the one
    /// this replaces, and the alternative — tying a process id back to its
    /// parent — needs WMI to do portably, which is a dependency this project
    /// will not take for one assertion.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RunAsync_WhenCancelled_ThrowsAndLeavesNoRunningChild()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var before = ProcessIdsNamed("PING");

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _runner.RunAsync(
                new ProcessRequest
                {
                    // ping rather than timeout: `timeout` refuses to run at
                    // all when standard output is redirected, so it exits
                    // before any cancellation could reach it - a sleeper that
                    // does not sleep would make this test pass for the wrong
                    // reason, or in this case fail loudly, which is how it was
                    // caught.
                    FileName = "ping",
                    Arguments = ["-n", "30", "127.0.0.1"],
                },
                cancellation.Token));

        (await SurvivorsOf(before)).ShouldBeEmpty();
    }

    /// <remarks>
    /// The child exiting between the cancellation and the kill is a race the
    /// runner swallows on purpose: the desired state was reached, and throwing
    /// would turn a success into a failure. Provoked with a token that fires
    /// after the process is already gone.
    /// </remarks>
    [Fact]
    public async Task RunAsync_WhenCancellationRacesTheChildExiting_DoesNotThrowFromTheKill()
    {
        using var cancellation = new CancellationTokenSource();

        var run = _runner.RunAsync(
            new ProcessRequest { FileName = "cmd", Arguments = ["/c", "exit", "0"] },
            cancellation.Token);

        await cancellation.CancelAsync();

        // Either outcome is correct — the process may have completed first, or
        // the cancellation may have won. Neither may be an unhandled exception
        // out of the kill path.
        try
        {
            (await run).ExitCode.ShouldBe(0);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// The process ids currently running under <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    /// Each handle is disposed. <c>GetProcessesByName</c> hands back live
    /// <see cref="System.Diagnostics.Process"/> objects, and a test that
    /// enumerates them in a loop and drops them leaks one operating-system
    /// handle per call — which the previous <c>.Length</c> did on every
    /// invocation.
    /// </remarks>
    private static HashSet<int> ProcessIdsNamed(string name)
    {
        var ids = new HashSet<int>();

        foreach (var process in System.Diagnostics.Process.GetProcessesByName(name))
        {
            using (process)
            {
                ids.Add(process.Id);
            }
        }

        return ids;
    }

    /// <summary>
    /// How long a killed process is given to disappear.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately far shorter than the child's own life, and that is the
    /// constraint that decides the number rather than generosity. The child is
    /// <c>ping -n 30</c>, which exits by itself after about twenty-nine
    /// seconds; a deadline anywhere near that would let a runner that never
    /// killed anything pass by simply outwaiting its own child, and the test
    /// would become a slow way of asserting nothing.
    /// </para>
    /// <para>
    /// Ten seconds is three times the margin, and reaping a process the
    /// operating system has already been told to kill takes milliseconds — so
    /// this is only ever paid in full by a run that is about to fail.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan ReapingDeadline = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Process ids that appeared during the run and are still there.
    /// </summary>
    /// <remarks>
    /// Polled rather than sampled once, because the thing being waited for is
    /// asynchronous by nature. It returns as soon as nothing survives.
    /// </remarks>
    private static async Task<IReadOnlyList<int>> SurvivorsOf(HashSet<int> before)
    {
        var deadline = DateTime.UtcNow + ReapingDeadline;

        while (true)
        {
            var survivors = ProcessIdsNamed("PING").Except(before).ToArray();

            if (survivors.Length == 0 || DateTime.UtcNow >= deadline)
            {
                return survivors;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
    }
}
