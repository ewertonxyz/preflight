namespace Preflight.Cli.Tests.Commands;

using NSubstitute;
using Preflight.Cli.Commands;
using Preflight.Cli.Interactive;
using Preflight.Cli.Model;
using Preflight.Cli.Pipelines;
using Preflight.Cli.Policy;
using Preflight.Cli.Services;
using Preflight.Cli.Storage;

/// <summary>
/// Fixes what <c>pipeline declare</c> and <c>pipeline use</c> do when the
/// command line did not say which pipeline, or which version.
/// </summary>
/// <remarks>
/// Every picker has a twin: the same selection is reachable by typing it, and
/// the two paths must produce the same result. That is the assertion that keeps
/// the interactive path from becoming a second, differently-behaved command —
/// and it is what makes CI, which can never prompt, a first-class caller rather
/// than a degraded one.
/// </remarks>
public sealed class PipelinePickedCommandTests : IDisposable
{
    private readonly DirectoryInfo _workspace = Directory.CreateTempSubdirectory("preflight-picked-");
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("preflight-picked-root-");
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly IWorkspaceFileWriter _writer = Substitute.For<IWorkspaceFileWriter>();
    private readonly IInstalledPipelineReader _installed = Substitute.For<IInstalledPipelineReader>();

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _workspace.Delete(recursive: true);
        _root.Delete(recursive: true);
    }

    private static PackageVersion Version(string text)
    {
        PackageVersion.TryParse(text, out var version).ShouldBeTrue();

        return version!;
    }

    private CommandEnvironment Environment(IPipelinePicker? picker, MachineState? state = null) =>
        CommandEnvironments.For(
            _workspace,
            _output,
            _error,
            TimeProvider.System,
            workspaceWriter: _writer,
            installRoot: new PipelineInstallRoot(_root),
            installedPipelines: _installed,
            machineState: state,
            picker: picker,
            isInputInteractive: picker is not null);

    private string Captured() =>
        (string)_writer.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IWorkspaceFileWriter.WriteNewAsync))
            .GetArguments()[1]!;

    private static IPipelinePicker Choosing(string value, Action<SelectionModel>? inspect = null)
    {
        var picker = Substitute.For<IPipelinePicker>();

        picker.Pick(Arg.Any<SelectionModel>()).Returns(call =>
        {
            inspect?.Invoke(call.ArgAt<SelectionModel>(0));

            return value;
        });

        return picker;
    }

    [Fact]
    public async Task DeclareAsync_WithoutAName_OffersWhatIsInstalledAndWritesWhatWasChosen()
    {
        _installed.Pipelines().Returns(["projecta", "projectb"]);

        SelectionModel? shown = null;

        (await PipelineCommandHandler.DeclareAsync(
            Environment(Choosing("projectb", model => shown = model)),
            name: null,
            TestContext.Current.CancellationToken))
            .ShouldBe(0);

        shown.ShouldNotBeNull();
        shown.Choices.Select(choice => choice.Value).ShouldBe(["projecta", "projectb"]);

        Captured().ShouldContain("\"pipeline\": \"projectb\"");
    }

    /// <remarks>
    /// The refusal comes before the question. A prompt whose answer the next
    /// line throws away has wasted somebody's attention, and it would ask about
    /// a file this command was never going to write.
    /// </remarks>
    [Fact]
    public async Task DeclareAsync_WithoutAName_WhenTheFileExists_RefusesWithoutAsking()
    {
        _writer.Exists(Arg.Any<string>()).Returns(true);
        _installed.Pipelines().Returns(["projecta"]);

        var picker = Substitute.For<IPipelinePicker>();

        await Should.ThrowAsync<PipelineCommandException>(() => PipelineCommandHandler.DeclareAsync(
            Environment(picker), name: null, TestContext.Current.CancellationToken));

        picker.DidNotReceiveWithAnyArgs().Pick(default!);
    }

    [Fact]
    public async Task DeclareAsync_WithoutAName_AndNoWayToAsk_IsTwo()
    {
        _installed.Pipelines().Returns(["projecta"]);

        var exception = await Should.ThrowAsync<NoInteractiveInputException>(
            () => PipelineCommandHandler.DeclareAsync(
                Environment(picker: null), name: null, TestContext.Current.CancellationToken));

        ExitCode.ForException(exception).ShouldBe(ExitCode.ConfigurationError);

        await _writer.DidNotReceiveWithAnyArgs()
            .WriteNewAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    /// <remarks>
    /// The pipeline comes from the checkout and the picker chooses only the
    /// version. Letting it choose the pipeline too would move that answer onto
    /// the machine, and a developer and CI would then validate against different
    /// rules with nothing in the header saying so.
    /// </remarks>
    [Fact]
    public async Task UseAsync_WithoutAnArgument_OffersTheCheckoutsPipelineVersions()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_workspace.FullName, PolicyResolution.BaseFileName),
            """{ "schemaVersion": 1, "pipeline": "projecta" }""",
            TestContext.Current.CancellationToken);

        _installed.Versions("projecta").Returns([Version("1.4.0"), Version("1.10.0")]);

        SelectionModel? shown = null;

        (await PipelineCommandHandler.UseAsync(
            Environment(Choosing("projecta@1.10.0", model => shown = model)),
            argument: null,
            TestContext.Current.CancellationToken))
            .ShouldBe(0);

        shown.ShouldNotBeNull();
        shown.Choices.Select(choice => choice.Value)
            .ShouldBe(["projecta@1.10.0", "projecta@1.4.0"]);

        new MachineStateStore()
            .Read(new PipelineInstallRoot(_root).MachineStatePath)
            .Pins["projecta"].ToString()
            .ShouldBe("1.10.0");
    }

    /// <summary>
    /// Typing the selector and picking it produce the same pin.
    /// </summary>
    /// <remarks>
    /// Every picker has a twin, and the twin does exactly the same thing. If
    /// the two ever diverged the interactive path would be a second command
    /// wearing the first one's name, and the machine that cannot prompt — every
    /// build agent — would be running something else.
    /// </remarks>
    [Fact]
    public async Task TheNonInteractiveTwin_ProducesTheSamePinAsThePicker()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_workspace.FullName, PolicyResolution.BaseFileName),
            """{ "schemaVersion": 1, "pipeline": "projecta" }""",
            TestContext.Current.CancellationToken);

        _installed.Versions("projecta").Returns([Version("1.4.0"), Version("1.10.0")]);

        (await PipelineCommandHandler.UseAsync(
            Environment(Choosing("projecta@1.4.0")),
            argument: null,
            TestContext.Current.CancellationToken))
            .ShouldBe(0);

        var picked = new MachineStateStore()
            .Read(new PipelineInstallRoot(_root).MachineStatePath);

        File.Delete(new PipelineInstallRoot(_root).MachineStatePath);

        (await PipelineCommandHandler.UseAsync(
            Environment(picker: null),
            "projecta@1.4.0",
            TestContext.Current.CancellationToken))
            .ShouldBe(0);

        new MachineStateStore()
            .Read(new PipelineInstallRoot(_root).MachineStatePath)
            .Pins["projecta"]
            .ShouldBe(picked.Pins["projecta"]);
    }

    [Fact]
    public async Task UseAsync_WithAPipelineNameAndNoVersion_OffersThatPipelinesVersions()
    {
        _installed.Versions("projectb").Returns([Version("2.0.0")]);

        (await PipelineCommandHandler.UseAsync(
            Environment(Choosing("projectb@2.0.0")),
            "projectb",
            TestContext.Current.CancellationToken))
            .ShouldBe(0);

        new MachineStateStore()
            .Read(new PipelineInstallRoot(_root).MachineStatePath)
            .Pins["projectb"].ToString()
            .ShouldBe("2.0.0");
    }

    [Fact]
    public async Task UseAsync_WithoutAnArgumentInACheckoutThatNamesNoPipeline_RefusesWithoutAsking()
    {
        var picker = Substitute.For<IPipelinePicker>();

        (await Should.ThrowAsync<PipelineCommandException>(() => PipelineCommandHandler.UseAsync(
            Environment(picker), argument: null, TestContext.Current.CancellationToken)))
            .Message.ShouldContain("pipeline declare");

        picker.DidNotReceiveWithAnyArgs().Pick(default!);
    }

    /// <remarks>
    /// A directory where the file goes, or a read-only workspace, is the
    /// workspace's condition and not a defect in this tool. Exit 3 would say the
    /// tool broke and send the wrong person to look.
    /// </remarks>
    [Fact]
    public async Task DeclareAsync_WhenTheWriteFails_IsTwoNotThree()
    {
        _writer.Exists(Arg.Any<string>()).Returns(false);
        _writer.WriteNewAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new UnauthorizedAccessException("read-only"));

        var exception = await Should.ThrowAsync<PipelineCommandException>(
            () => PipelineCommandHandler.DeclareAsync(
                Environment(picker: null), "projecta", TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("read-only");
        ExitCode.ForException(exception).ShouldBe(ExitCode.ConfigurationError);
        _output.ToString().ShouldNotContain("Wrote");
    }
}
