namespace Preflight.Cli.Tests.Interactive;

using NSubstitute;
using Preflight.Cli.Commands;
using Preflight.Cli.Interactive;
using Preflight.Cli.Tests.Commands;

/// <summary>
/// Fixes when a person is asked, and when the tool refuses instead.
/// </summary>
/// <remarks>
/// The refusals are the point. A prompt that falls back to a default is a
/// selection nobody made deciding what gets validated, which is what ADR-029
/// spent a whole decision refusing one floor up; ADR-035 refuses it again here.
/// Every row below is a state in which there is nobody to answer.
/// </remarks>
public sealed class PipelinePickerTests : IDisposable
{
    private readonly DirectoryInfo _workspace = Directory.CreateTempSubdirectory("preflight-picker-");
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    private static readonly SelectionModel TwoChoices = new(
        [
            new SelectionChoice("1.4.0", "projecta@1.4.0", IsActive: true, IsAllowed: true),
            new SelectionChoice("2.0.0", "projecta@2.0.0", IsActive: false, IsAllowed: true),
        ],
        ActiveIndex: 0,
        "Which version?");

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _workspace.Delete(recursive: true);
    }

    private CommandEnvironment Environment(
        bool isInputInteractive, IPipelinePicker? picker = null, IEnvironmentReader? reader = null) =>
        CommandEnvironments.For(
            _workspace,
            _output,
            _error,
            TimeProvider.System,
            reader: reader,
            picker: picker,
            isInputInteractive: isInputInteractive);

    /// <remarks>
    /// stdout is interactive here and stdin is not, which is precisely
    /// <c>echo 1 | preflight pipeline use</c>. Asking the stdout half would let
    /// this reach a prompt that reads a byte somebody's script meant for
    /// something else.
    /// </remarks>
    [Fact]
    public void Choose_WithoutAnInteractiveStdin_Refuses()
    {
        var picker = Substitute.For<IPipelinePicker>();

        var exception = Should.Throw<NoInteractiveInputException>(
            () => PipelinePicker.Choose(Environment(isInputInteractive: false, picker), TwoChoices));

        exception.Message.ShouldContain("projecta@1.4.0");
        picker.DidNotReceiveWithAnyArgs().Pick(default!);
    }

    [Theory]
    [InlineData("CI")]
    [InlineData("TEAMCITY_VERSION")]
    [InlineData("GITHUB_ACTIONS")]
    [InlineData("BUILD_BUILDID")]
    [InlineData("JENKINS_URL")]
    public void Choose_UnderCi_RefusesNamingTheVariable(string variable)
    {
        var reader = Substitute.For<IEnvironmentReader>();

        reader.GetVariable(Arg.Any<string>()).Returns((string?)null);
        reader.GetVariable(variable).Returns("1");

        var picker = Substitute.For<IPipelinePicker>();

        var exception = Should.Throw<NoInteractiveInputException>(() => PipelinePicker.Choose(
            Environment(isInputInteractive: true, picker, reader), TwoChoices));

        exception.Message.ShouldContain(variable);
        picker.DidNotReceiveWithAnyArgs().Pick(default!);
    }

    [Fact]
    public void Choose_WithNothingToChooseFrom_RefusesRatherThanShowingAnEmptyMenu()
    {
        var empty = new SelectionModel([], ActiveIndex: 0, "Which version?");

        Should.Throw<NoInteractiveInputException>(
            () => PipelinePicker.Choose(Environment(isInputInteractive: true), empty))
            .Message.ShouldContain("pipeline install");
    }

    /// <remarks>
    /// The pipeline selector already decided that one candidate is not a choice
    /// anybody made, and adopted nothing. The same rule holds here: the single
    /// row is shown and confirmed, never taken because it was the only one on
    /// the list. Adopting it would be the selector's silence wearing a menu's
    /// clothes.
    /// </remarks>
    [Fact]
    public void Choose_WithExactlyOneCandidate_AsksAnywayRatherThanAdoptingIt()
    {
        var only = new SelectionModel(
            [new SelectionChoice("1.4.0", "projecta@1.4.0", IsActive: false, IsAllowed: true)],
            ActiveIndex: 0,
            "Which version?");

        var picker = Substitute.For<IPipelinePicker>();

        picker.Pick(Arg.Any<SelectionModel>()).Returns("projecta@1.4.0");

        PipelinePicker.Choose(Environment(isInputInteractive: true, picker), only)
            .ShouldBe("projecta@1.4.0");

        picker.Received(1).Pick(only);
    }

    [Fact]
    public void Choose_WithSomebodyToAsk_ReturnsWhatTheyChose()
    {
        var picker = Substitute.For<IPipelinePicker>();

        picker.Pick(Arg.Any<SelectionModel>()).Returns("projecta@2.0.0");

        PipelinePicker.Choose(Environment(isInputInteractive: true, picker), TwoChoices)
            .ShouldBe("projecta@2.0.0");
    }
}
