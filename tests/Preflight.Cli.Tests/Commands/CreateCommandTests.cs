namespace Preflight.Cli.Tests.Commands;

using System.Text;
using NSubstitute;
using Preflight.Cli.Commands;
using Preflight.Cli.Model;
using Preflight.Cli.Services;
using Preflight.Rules;

/// <summary>
/// Fixes what <c>preflight create workspace</c> writes, what it refuses, and
/// what it declines to find out on its own.
/// </summary>
/// <remarks>
/// The refusal is asserted by non-invocation rather than by comparing the file
/// afterwards: a write that happened to restore the original bytes would pass a
/// comparison and still be the defect.
///
/// A refusal is raised, not returned. Every command in this tool reports a
/// configuration problem by throwing through the boundary in
/// <c>PreflightCommandLine.Execute</c>, which is the one place that maps it to
/// an exit code; the mapping is asserted here so the pairing cannot drift, and
/// the exit code itself is proved end to end by the specification.
/// </remarks>
public sealed class CreateCommandTests : IDisposable
{
    private readonly DirectoryInfo _workspace = Directory.CreateTempSubdirectory("preflight-create-");
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly IWorkspaceFileWriter _writer = Substitute.For<IWorkspaceFileWriter>();

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _workspace.Delete(recursive: true);
    }

    private CommandEnvironment Environment() => CommandEnvironments.For(
        _workspace,
        _output,
        _error,
        TimeProvider.System,
        workspaceWriter: _writer);

    private string ManifestPath => Path.Combine(_workspace.FullName, WorkspaceManifest.DefaultFileName);

    private Task<int> Invoke() =>
        CreateCommandHandler.WorkspaceAsync(Environment(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task WorkspaceAsync_InAnEmptyWorkspace_WritesTheSkeletonAndIsZero()
    {
        _writer.Exists(Arg.Any<string>()).Returns(false);

        (await Invoke()).ShouldBe(0);

        await _writer.Received(1).WriteNewAsync(ManifestPath, Arg.Any<string>(), Arg.Any<CancellationToken>());
        _output.ToString().ShouldContain(WorkspaceManifest.DefaultFileName);
    }

    /// <remarks>
    /// Non-invocation is the assertion. Reading the file back afterwards would
    /// pass just as well against an implementation that rewrote it with the
    /// same bytes, and "never overwrites" is a promise about the write, not
    /// about the outcome.
    /// </remarks>
    [Fact]
    public async Task WorkspaceAsync_WhenTheFileAlreadyExists_RefusesAndWritesNothing()
    {
        _writer.Exists(ManifestPath).Returns(true);

        var exception = await Should.ThrowAsync<WorkspaceFileExistsException>(Invoke);

        exception.Message.ShouldContain(WorkspaceManifest.DefaultFileName);
        ExitCode.ForException(exception).ShouldBe(ExitCode.ConfigurationError);

        await _writer.DidNotReceiveWithAnyArgs()
            .WriteNewAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The command finds out nothing about the project it is run in.
    /// </summary>
    /// <remarks>
    /// A manifest that arrived pre-filled is a manifest nobody reads before
    /// trusting, and what it would be filled from is a guess: the tool cannot
    /// know what a build actually consumes, and the same refusal to infer is
    /// what keeps the cache from serving a stale pass. What this command writes
    /// is a commented skeleton, empty of facts. The two workspaces here differ
    /// in everything a detector would look at and must produce identical bytes.
    /// </remarks>
    [Fact]
    public async Task WorkspaceAsync_InAProjectFullOfSolutionAndUprojectFiles_EmitsNoFactAboutAnyOfThem()
    {
        File.WriteAllText(Path.Combine(_workspace.FullName, "Game.sln"), string.Empty);
        File.WriteAllText(Path.Combine(_workspace.FullName, "CMakeLists.txt"), string.Empty);
        File.WriteAllText(Path.Combine(_workspace.FullName, "Game.uproject"), string.Empty);
        _writer.Exists(Arg.Any<string>()).Returns(false);

        string? written = null;
        await _writer.WriteNewAsync(
            Arg.Any<string>(),
            Arg.Do<string>(content => written = content),
            Arg.Any<CancellationToken>());

        (await Invoke()).ShouldBe(0);

        written.ShouldNotBeNull();
        written.ShouldNotContain("Game.sln");
        written.ShouldNotContain("CMakeLists.txt");
        written.ShouldNotContain("Game.uproject");
        written.ShouldBe(CreateCommandHandler.Skeleton);
    }

    /// <remarks>
    /// A write that fails is still a configuration outcome and not an internal
    /// error: the disk is full, or the directory is read-only, and neither is
    /// the tool breaking. 3 would send the wrong person to look.
    /// </remarks>
    [Fact]
    public async Task WorkspaceAsync_WhenTheWriterThrows_IsAConfigurationErrorAndReportsNoSuccess()
    {
        _writer.Exists(Arg.Any<string>()).Returns(false);
        _writer.WriteNewAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new IOException("disk full"));

        var exception = await Should.ThrowAsync<WorkspaceFileExistsException>(Invoke);

        exception.Message.ShouldContain("disk full");
        ExitCode.ForException(exception).ShouldBe(ExitCode.ConfigurationError);
        _output.ToString().ShouldNotContain("Wrote");
    }

    /// <summary>
    /// The generated skeleton is a manifest this tool can read.
    /// </summary>
    /// <remarks>
    /// The skeleton is commented, and the manifest has its own reader with its
    /// own options. A skeleton the product itself rejects is the likeliest way
    /// this command ships broken, and it would only surface on the user's next
    /// run.
    /// </remarks>
    [Fact]
    public async Task WorkspaceAsync_TheGeneratedSkeleton_ParsesAsAWorkspaceManifest()
    {
        var path = Path.Combine(_workspace.FullName, "generated.json");
        await File.WriteAllTextAsync(
            path, CreateCommandHandler.Skeleton, Encoding.UTF8, TestContext.Current.CancellationToken);

        var manifest = await WorkspaceManifest.LoadAsync(
            new Preflight.Core.PhysicalFileSystem(), path, TestContext.Current.CancellationToken);

        manifest.ShouldNotBeNull();
        manifest.Tools.ShouldBeEmpty();
        manifest.Dependencies.ShouldBeEmpty();
        manifest.CompileProbe.ShouldBeNull();
    }
}
