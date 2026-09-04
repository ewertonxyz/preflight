namespace Preflight.Rules.Tests.Build;

using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// Fixes <see cref="BuildConfigurationRule"/>, whose finding the console
/// reporter uses as its worked example.
/// </summary>
/// <remarks>
/// One test below pins four strings that goldens in another project quote. The
/// example report every reporter renders is this rule missing
/// <c>contentRoot</c>, word for word — but those goldens render a hand-written
/// copy of the finding rather than calling this rule, so they will not object
/// when the wording drifts here. Only this test will, which makes it the one
/// place the two are held together and the reason it asserts exact strings
/// instead of "it failed".
/// </remarks>
public sealed class BuildConfigurationRuleTests
{
    private static readonly string[] ThreeRequiredKeys = ["contentRoot", "shaderCache", "audioBank"];

    private static readonly string[] TwoMissingKeys = ["zeta", "alpha"];

    private readonly BuildConfigurationRule _rule = new();

    private static IFileSystem WorkspaceWith(string? configuration, params string[] existingPaths)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        fileSystem.DirectoryExists(Arg.Any<string>()).Returns(false);

        if (configuration is not null)
        {
            fileSystem.FileExists(Arg.Is<string>(path => path.EndsWith(".json", StringComparison.Ordinal)))
                .Returns(true);
            fileSystem.ReadAllTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(configuration);
        }

        foreach (var existing in existingPaths)
        {
            fileSystem.DirectoryExists(Arg.Is<string>(path => path.EndsWith(existing, StringComparison.Ordinal)))
                .Returns(true);
        }

        return fileSystem;
    }

    private Task<RuleOutcome> Run(
        IFileSystem fileSystem,
        BuildTarget? target = null,
        IPolicyReader? policy = null) =>
        _rule.ExecuteAsync(
            Context(
                policy: policy,
                fileSystem: fileSystem,
                stage: ValidationStage.BuildReadiness,
                target: target),
            CancellationToken.None);

    [Fact]
    public async Task ExecuteAsync_WithACompleteAndCoherentConfiguration_Passes()
    {
        var outcome = await Run(
            WorkspaceWith("""{ "contentRoot": "content/win64" }""", "content/win64"));

        outcome.Status.ShouldBe(RuleStatus.Passed);
    }

    /// <summary>
    /// The finding the reporters' worked examples quote, word for word.
    /// </summary>
    /// <remarks>
    /// Asserting the four exact strings rather than "it failed" is the whole
    /// point of this test. A looser assertion would stay green through a
    /// reworded remediation, and nothing else would object: the console, JSON
    /// and SARIF goldens two projects away render a hand-written copy of this
    /// finding held in the reporter fixture, so they would go on showing the
    /// old wording as what the tool says. This test failing is the reminder to
    /// carry the new wording into that copy.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithoutContentRoot_ReportsTheFindingTheWorkedExamplesQuote()
    {
        var outcome = await Run(WorkspaceWith("""{ "platform": "win64" }"""));

        outcome.Status.ShouldBe(RuleStatus.Failed);

        var finding = outcome.Findings.ShouldHaveSingleItem();

        finding.Message.ShouldBe("Missing platform configuration entry.");
        finding.Location!.RelativePath.ShouldBe("config/build/win64.json");
        finding.Expected.ShouldBe("a \"contentRoot\" entry");
        finding.Actual.ShouldBe("key not present");
        finding.Remediation.ShouldBe(
            "Add \"contentRoot\" to the configuration, or ask the pipeline's author " +
            "to drop it from 'requiredKeys'.");
    }

    /// <summary>
    /// Complete is not the same as coherent.
    /// </summary>
    /// <remarks>
    /// A configuration naming a content folder that is not there is formally
    /// valid and produces a build that fails much later, with an error about a
    /// missing asset rather than about a wrong path. Checking only for the
    /// key's presence would let that through.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithContentRootPointingNowhere_Fails()
    {
        var outcome = await Run(WorkspaceWith("""{ "contentRoot": "content/win64" }"""));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Expected.ShouldNotBeNull().ShouldContain("content/win64");
    }

    /// <remarks>
    /// The tokens are what make one rule serve every platform. Without them a
    /// production shipping for three platforms needs three rules, or three
    /// policy overlays saying the same thing three ways.
    /// </remarks>
    [Theory]
    [InlineData("win64", "Development", "config/build/win64.json")]
    [InlineData("linux64", "Shipping", "config/build/linux64.json")]
    public async Task ExecuteAsync_ResolvesThePathFromTheTarget(
        string platform,
        string configuration,
        string expectedPath)
    {
        var fileSystem = WorkspaceWith(null);

        await Run(fileSystem, new BuildTarget(platform, configuration));

        // Path.Combine does not rewrite separators inside a segment, so the
        // template's forward slashes survive on Windows too. Asserting the
        // platform separator here would pass on Linux and fail on the machine
        // this project is developed on.
        fileSystem.Received().FileExists(
            Arg.Is<string>(path => path.EndsWith(expectedPath, StringComparison.Ordinal)));
    }

    [Fact]
    public void Resolve_FillsBothTokens()
    {
        BuildConfigurationRule
            .Resolve("build/{platform}/{configuration}.json", new BuildTarget("win64", "Shipping"))
            .ShouldBe("build/win64/Shipping.json");
    }

    [Fact]
    public async Task ExecuteAsync_WithNoConfigurationFile_FailsNamingTheTarget()
    {
        var outcome = await Run(WorkspaceWith(null));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Expected.ShouldNotBeNull().ShouldContain("win64/Development");
    }

    /// <remarks>
    /// A location with a line number, because malformed JSON has one and a
    /// report that omits it makes the reader search a file the tool already
    /// read.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithMalformedJson_FailsWithALocationRatherThanThrowing()
    {
        var outcome = await Run(WorkspaceWith("{ \"contentRoot\": }"));

        outcome.Status.ShouldBe(RuleStatus.Failed);

        var finding = outcome.Findings.ShouldHaveSingleItem();

        finding.Location!.RelativePath.ShouldBe("config/build/win64.json");
        finding.Location.Line.ShouldNotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithAConfigurationThatIsNotAnObject_Fails()
    {
        var outcome = await Run(WorkspaceWith("[1, 2, 3]"));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Actual.ShouldBe("Array");
    }

    [Fact]
    public async Task ExecuteAsync_ReadsTheRequiredKeysFromPolicy()
    {
        var outcome = await Run(
            WorkspaceWith("""{ "contentRoot": "content", "shaderCache": "shaders" }""", "content", "shaders"),
            policy: PolicyWith("requiredKeys", ThreeRequiredKeys));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Expected.ShouldNotBeNull().ShouldContain("audioBank");
    }

    /// <remarks>
    /// A key that is present but is a number, or an empty string, is not a path
    /// this rule can check. Completeness already covered its presence;
    /// inventing a coherence verdict from a value it cannot interpret would be
    /// the rule asserting something nobody told it.
    /// </remarks>
    [Theory]
    [InlineData("""{ "contentRoot": 42 }""")]
    [InlineData("""{ "contentRoot": "" }""")]
    [InlineData("""{ "contentRoot": "   " }""")]
    public async Task ExecuteAsync_WithAPathKeyThatIsNotAUsablePath_DoesNotInventACoherenceFailure(string json)
    {
        (await Run(WorkspaceWith(json))).Status.ShouldBe(RuleStatus.Passed);
    }

    /// <remarks>
    /// A path key may name a file rather than a directory — a packed archive, a
    /// manifest. Accepting only directories would fail every production that
    /// ships content as one file.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_AcceptsAFileAsAPathTarget()
    {
        var fileSystem = WorkspaceWith("""{ "contentRoot": "content.pak" }""");

        fileSystem.FileExists(Arg.Is<string>(path => path.EndsWith("content.pak", StringComparison.Ordinal)))
            .Returns(true);

        (await Run(fileSystem)).Status.ShouldBe(RuleStatus.Passed);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsEveryMissingKey_InTheOrderDeclared()
    {
        var outcome = await Run(
            WorkspaceWith("{ }"),
            policy: PolicyWith("requiredKeys", TwoMissingKeys));

        outcome.Findings.Select(finding => finding.Expected)
            .ShouldBe(["a \"zeta\" entry", "a \"alpha\" entry"]);
    }

    [Fact]
    public async Task ExecuteAsync_WithACancelledToken_StopsRatherThanFinishing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _rule.ExecuteAsync(
                Context(
                    fileSystem: WorkspaceWith("{ }"),
                    stage: ValidationStage.BuildReadiness),
                cancellation.Token));
    }
}
