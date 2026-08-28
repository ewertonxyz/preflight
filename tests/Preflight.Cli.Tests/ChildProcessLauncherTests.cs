namespace Preflight.Cli.Tests;

using System.Text;
using Preflight.Core;

/// <summary>
/// The wrapper of the measure wrapper, against a real child process.
/// </summary>
/// <remarks>
/// <para>
/// The only place the propagation contract is observable. In-process tests
/// substitute the launcher and assert what <c>measure</c> does with what it is
/// given; nothing below this class can show that a real child's bytes and exit
/// code survive the trip.
/// </para>
/// <para>
/// The child is <c>dotnet --version</c> and a deliberately missing binary,
/// because both are available on any machine that can build this project. The
/// exit-code row uses an unknown SDK flag, whose non-zero code is the
/// SDK's rather than one this test chose — which is the point: the wrapper must
/// not know or care.
/// </para>
/// </remarks>
public sealed class ChildProcessLauncherTests
{
    [Fact]
    public async Task RunAsync_ForASucceedingChild_ReturnsZeroAndPropagatesItsOutput()
    {
        var output = new MemoryStream();
        var error = new MemoryStream();

        var exitCode = await new ChildProcessLauncher().RunAsync(
            new ChildProcessRequest("dotnet", ["--version"], Directory.GetCurrentDirectory()),
            output,
            error,
            TestContext.Current.CancellationToken);

        exitCode.ShouldBe(0);
        Encoding.UTF8.GetString(output.ToArray()).Trim().ShouldNotBeEmpty();
    }

    /// <remarks>
    /// The exit code is whatever the child returned. A wrapper that normalised
    /// it would change the behaviour of every script it was dropped into. A
    /// measurement that alters what it measures is useless.
    /// </remarks>
    [Fact]
    public async Task RunAsync_ForAFailingChild_ReturnsItsNonZeroExitCode()
    {
        var exitCode = await new ChildProcessLauncher().RunAsync(
            new ChildProcessRequest("dotnet", ["--nonsense-flag"], Directory.GetCurrentDirectory()),
            Stream.Null,
            Stream.Null,
            TestContext.Current.CancellationToken);

        exitCode.ShouldNotBe(0);
    }

    /// <remarks>
    /// A <see cref="ProcessLaunchException"/> rather than a Win32 error code,
    /// so the caller can turn it into 127 and say the name of the thing that is
    /// missing.
    /// </remarks>
    [Fact]
    public async Task RunAsync_ForAChildThatCannotBeStarted_ThrowsNamingIt()
    {
        var exception = await Should.ThrowAsync<ProcessLaunchException>(() =>
            new ChildProcessLauncher().RunAsync(
                new ChildProcessRequest("preflight-no-such-binary", [], Directory.GetCurrentDirectory()),
                Stream.Null,
                Stream.Null,
                TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("preflight-no-such-binary");
    }

    [Fact]
    public void Describe_JoinsTheCommandForTheRecordWithoutQuoting() =>
        new ChildProcessRequest("msbuild", ["Game.sln", "/p:Configuration=Development"], ".")
            .Describe()
            .ShouldBe("msbuild Game.sln /p:Configuration=Development");
}
