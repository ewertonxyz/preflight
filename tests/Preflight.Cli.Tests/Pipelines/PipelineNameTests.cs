namespace Preflight.Cli.Tests.Pipelines;

using Preflight.Cli.Pipelines;
using Preflight.Core.Policy;

/// <summary>
/// Fixes the one rule for what may be a pipeline name, and the two messages that
/// state it.
/// </summary>
/// <remarks>
/// The name becomes a file name and, since the install root exists, a directory
/// outside the workspace. The corpus below is the set of things that must never
/// get that far.
/// </remarks>
public sealed class PipelineNameTests
{
    [Theory]
    [InlineData("../evil", false)]
    [InlineData("", false)]
    [InlineData("a b", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("a:b", false)]
    [InlineData("atlas\0", false)]
    [InlineData("atlas", true)]
    [InlineData("project-a", true)]
    [InlineData("project_a", true)]
    [InlineData("Atlas2", true)]
    public void IsValid_OverTheLabelCorpus_MatchesTheDocumentedRule(string name, bool expected) =>
        PipelineName.IsValid(name).ShouldBe(expected);

    [Fact]
    public void IsValid_ForALongName_IsStillDecidedByTheCharacters() =>
        PipelineName.IsValid(new string('a', 300)).ShouldBeTrue();

    [Fact]
    public void Require_ForAValidName_DoesNotThrow() => PipelineName.Require("atlas");

    /// <remarks>
    /// The two messages stay distinct on purpose, and this is the test that says
    /// so out loud: a name typed at the command line needs no file named back at
    /// the person who just typed it, while a name read out of a versioned file
    /// needs it as the first thing in the sentence. A refactor that unifies the
    /// two wordings is exactly what this catches.
    /// </remarks>
    [Fact]
    public void Require_FromTheFlagAndFromTheCheckoutFile_KeepTheirTwoDistinctMessages()
    {
        var fromFlag = Should.Throw<PolicyValidationException>(() => PipelineName.Require("../evil"));

        var fromFile = Should.Throw<PolicyValidationException>(
            () => PipelineName.Require("../evil", "preflight.base.json", "C:\\ws\\preflight.base.json"));

        fromFlag.Message.ShouldNotBe(fromFile.Message);
        fromFile.Message.ShouldContain("preflight.base.json");
        fromFlag.Message.ShouldNotContain("preflight.base.json");
    }
}
