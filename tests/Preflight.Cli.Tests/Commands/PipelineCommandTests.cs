namespace Preflight.Cli.Tests.Commands;

using NSubstitute;
using Preflight.Cli.Commands;
using Preflight.Core.Policy;

/// <summary>
/// Fixes <c>pipeline declare</c>, <c>use</c> and <c>list</c>.
/// </summary>
/// <remarks>
/// The pair worth reading together is <c>declare</c>, which refuses to touch an
/// existing file, and <c>use</c>, which overwrites every time. They are two
/// commands because they write to two different places for two different
/// audiences, and the opposite semantics are asserted here so that a later
/// harmonisation breaks. See ADR-035.
/// </remarks>
public sealed class PipelineCommandTests : IDisposable
{
    private readonly DirectoryInfo _workspace = Directory.CreateTempSubdirectory("preflight-pipe-cmd-");
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("preflight-pipe-root-");
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly IWorkspaceFileWriter _writer = Substitute.For<IWorkspaceFileWriter>();

    public void Dispose()
    {
        _workspace.Delete(recursive: true);
        _root.Delete(recursive: true);
    }

    [Fact]
    public async Task DeclareAsync_WritesAFileThatSelectsThePipeline()
    {
        (await Declare("projecta")).ShouldBe(0);

        var content = Captured();

        content.ShouldContain("\"pipeline\": \"projecta\"");
        PolicyDocument.Parse(content, "preflight.base.json").TryGetRaw("pipeline", out var value)
            .ShouldBeTrue();
        value.ShouldBe("projecta");
    }

    /// <remarks>
    /// The comments are the reason the file is never rewritten, so the generated
    /// one has to carry some for the argument to be about anything.
    /// </remarks>
    [Fact]
    public async Task DeclareAsync_WritesAFileThatKeepsItsComments()
    {
        await Declare("projecta");

        Captured().ShouldContain("//");
    }

    /// <remarks>
    /// With nothing installed, an active range would produce a file whose next
    /// run is exit 2 — the command would break the workspace it had just set up.
    /// </remarks>
    [Fact]
    public async Task DeclareAsync_WithNothingInstalled_LeavesTheRangeCommented()
    {
        await Declare("projecta");

        var content = Captured();

        content.ShouldContain("// \"requiresPipeline\"");
        PolicyDocument.Parse(content, "preflight.base.json")
            .TryGetRaw("requiresPipeline.minimumVersion", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task DeclareAsync_WithAPackageInstalled_WritesAnActiveRange()
    {
        var reader = Substitute.For<IInstalledPipelineReader>();

        PackageVersion.TryParse("1.4.0", out var version).ShouldBeTrue();
        reader.Versions("projecta").Returns([version!]);

        await Declare("projecta", reader);

        var document = PolicyDocument.Parse(Captured(), "preflight.base.json");

        document.TryGetRaw("requiresPipeline.minimumVersion", out var minimum).ShouldBeTrue();
        document.TryGetRaw("requiresPipeline.maximumVersion", out var maximum).ShouldBeTrue();
        minimum.ShouldBe("1.4.0");
        maximum.ShouldBe("2.0.0");
    }

    [Fact]
    public async Task DeclareAsync_WhenTheFileExists_RefusesAndWritesNothing()
    {
        _writer.Exists(Arg.Any<string>()).Returns(true);

        await Should.ThrowAsync<PipelineCommandException>(() => Declare("projecta"));

        await _writer.DidNotReceiveWithAnyArgs()
            .WriteNewAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DeclareAsync_ForANameThatIsNotALabel_Refuses() =>
        await Should.ThrowAsync<PolicyValidationException>(() => Declare("../evil"));

    [Fact]
    public async Task UseAsync_ForAVersionThatIsNotInstalled_RefusesAtTheCommand()
    {
        var error = await Should.ThrowAsync<PipelineCommandException>(() => Use("projecta@1.9.0"));

        error.Message.ShouldContain("1.9.0");
        error.Message.ShouldContain("nothing is installed");
        File.Exists(new PipelineInstallRoot(_root).MachineStatePath).ShouldBeFalse();
    }

    /// <remarks>
    /// The other half of that message. "Nothing is installed for it" and a list
    /// of what is are two different next steps — install the package, or fix the
    /// version you typed — and a message that always said the first would send
    /// somebody looking for a package they already have.
    /// </remarks>
    [Fact]
    public async Task UseAsync_ForAVersionThatIsNotInstalledWhenOthersAre_ListsWhatIs()
    {
        var reader = Substitute.For<IInstalledPipelineReader>();

        PackageVersion.TryParse("1.4.0", out var installed).ShouldBeTrue();
        reader.Versions("projecta").Returns([installed!]);

        var error = await Should.ThrowAsync<PipelineCommandException>(
            () => Use("projecta@1.9.0", reader));

        error.Message.ShouldContain("installed: 1.4.0");
        error.Message.ShouldNotContain("nothing is installed");
    }

    /// <remarks>
    /// <c>"projecta"</c> was a row here until the picker arrived, and it moved
    /// rather than being deleted: a bare name is now the second half of a
    /// question — which version of it — and the test below holds it to the same
    /// exit code by the route that is now correct for it. Anything carrying an
    /// <c>@</c> is still a selector, and a selector still has to be whole.
    /// </remarks>
    [Theory]
    [InlineData("projecta@")]
    [InlineData("@1.4.0")]
    [InlineData("projecta@1.4")]
    public async Task UseAsync_ForAMalformedSelector_Refuses(string selector) =>
        await Should.ThrowAsync<PipelineCommandException>(() => Use(selector));

    /// <remarks>
    /// A bare name asks which version, and there is nobody to ask under a test
    /// host — as there is nobody to ask on a build agent. Still exit 2, and
    /// still no pin written: the refusal changed its sentence, not its
    /// consequence.
    /// </remarks>
    [Fact]
    public async Task UseAsync_ForABareNameWithNobodyToAsk_IsStillTwoAndWritesNoPin()
    {
        var exception = await Should.ThrowAsync<Preflight.Cli.Interactive.NoInteractiveInputException>(
            () => Use("projecta"));

        ExitCode.ForException(exception).ShouldBe(ExitCode.ConfigurationError);
        File.Exists(new PipelineInstallRoot(_root).MachineStatePath).ShouldBeFalse();
    }

    [Fact]
    public async Task UseAsync_OverAnExistingPin_Overwrites()
    {
        var reader = Substitute.For<IInstalledPipelineReader>();

        PackageVersion.TryParse("1.4.0", out var first).ShouldBeTrue();
        PackageVersion.TryParse("1.5.0", out var second).ShouldBeTrue();
        reader.Versions("projecta").Returns([first!, second!]);

        (await Use("projecta@1.4.0", reader)).ShouldBe(0);
        (await Use("projecta@1.5.0", reader, Reread())).ShouldBe(0);

        Reread().Pins["projecta"].ToString().ShouldBe("1.5.0");
    }

    /// <remarks>
    /// "Which version is active" without "and why" is the half of the answer
    /// that helps nobody decide what to do next, so both the pin and its absence
    /// are asserted here rather than only the version list.
    /// </remarks>
    [Fact]
    public async Task ListAsync_WithPipelinesInstalled_MarksThePinnedOneAndSaysWhenThereIsNone()
    {
        var reader = Substitute.For<IInstalledPipelineReader>();

        PackageVersion.TryParse("1.4.0", out var pinned).ShouldBeTrue();
        PackageVersion.TryParse("1.10.0", out var newer).ShouldBeTrue();

        reader.Pipelines().Returns(["projecta", "projectb"]);
        reader.Versions("projecta").Returns([pinned!, newer!]);
        reader.Versions("projectb").Returns([newer!]);

        var state = MachineState.Empty with
        {
            Pins = new Dictionary<string, PackageVersion>(StringComparer.OrdinalIgnoreCase)
            {
                ["projecta"] = pinned!,
            },
        };

        (await PipelineCommandHandler.ListAsync(
            Environment(reader, state), TestContext.Current.CancellationToken)).ShouldBe(0);

        var printed = _output.ToString();

        printed.ShouldContain("* 1.4.0");
        printed.ShouldContain("pinned");

        // projectb has no pin, so the line explaining what happens without one
        // has to appear for it.
        printed.ShouldContain("no pin");
    }

    [Fact]
    public async Task ListAsync_WithNothingInstalled_SaysSoAndSucceeds()
    {
        (await PipelineCommandHandler.ListAsync(
            Environment(), TestContext.Current.CancellationToken)).ShouldBe(0);

        _output.ToString().ShouldContain("No pipelines installed");
    }

    private string Captured() =>
        (string)_writer.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IWorkspaceFileWriter.WriteNewAsync))
            .GetArguments()[1]!;

    private MachineState Reread() => new MachineStateStore().Read(
        new PipelineInstallRoot(_root).MachineStatePath);

    private Task<int> Declare(string name, IInstalledPipelineReader? reader = null) =>
        PipelineCommandHandler.DeclareAsync(
            Environment(reader), name, TestContext.Current.CancellationToken);

    private Task<int> Use(
        string selector, IInstalledPipelineReader? reader = null, MachineState? state = null) =>
        PipelineCommandHandler.UseAsync(
            Environment(reader, state), selector, TestContext.Current.CancellationToken);

    private CommandEnvironment Environment(
        IInstalledPipelineReader? reader = null, MachineState? state = null) =>
        CommandEnvironments.For(
            _workspace,
            _output,
            _error,
            TimeProvider.System,
            workspaceWriter: _writer,
            installRoot: new PipelineInstallRoot(_root),
            installedPipelines: reader ?? Substitute.For<IInstalledPipelineReader>(),
            machineState: state);
}
