namespace Preflight.Core.Tests.Graph;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core;
using static Preflight.Core.Tests.Graph.GraphFixture;

/// <summary>
/// Fixes the missing-dependency error of the rule graph: it names
/// both ids and suggests the closest by edit distance.
/// </summary>
/// <remarks>
/// The suggestion comes from <c>SuggestionFinder</c>, built in the policy layer for
/// exactly this third call site. These tests check that its contract is passed
/// through faithfully — threshold and alphabetical tie-break included — rather
/// than reimplemented here with a different cutoff.
/// </remarks>
public sealed class RuleGraphMissingDependencyTests
{
    [Fact]
    public void Build_WithDependencyOnUnknownId_NamesBothIds()
    {
        var exception = Should.Throw<GraphValidationException>(() => RuleGraph.Build([
            Rule("core.build.compile-probe", "core.workspace.toolchian"),
            Rule("core.workspace.toolchain"),
        ]));

        var error = exception.Errors.ShouldHaveSingleItem()
            .ShouldBeOfType<GraphValidationError.MissingDependency>();

        error.RuleId.ShouldBe(new RuleId("core.build.compile-probe"));
        error.MissingTarget.ShouldBe(new RuleId("core.workspace.toolchian"));
        error.Message.ShouldContain("core.build.compile-probe");
        error.Message.ShouldContain("core.workspace.toolchian");
    }

    [Theory]
    [InlineData("core.workspace.toolchian")]
    [InlineData("core.workspace.toolchan")]
    public void Build_WithMissingDependencyHavingACloseMatch_SuggestsIt(string typo)
    {
        var exception = Should.Throw<GraphValidationException>(() => RuleGraph.Build([
            Rule("core.build.compile-probe", typo),
            Rule("core.workspace.toolchain"),
        ]));

        exception.Errors.ShouldHaveSingleItem()
            .ShouldBeOfType<GraphValidationError.MissingDependency>()
            .Suggestions.ShouldContain("core.workspace.toolchain");
    }

    /// <remarks>
    /// An empty suggestion list must still produce a clean message. A
    /// formatter that always appends "Did you mean" would end up asking "Did
    /// you mean ''?", which is worse than offering nothing.
    /// </remarks>
    [Fact]
    public void Build_WithMissingDependencyHavingNoCloseMatch_ReturnsNoSuggestions()
    {
        var exception = Should.Throw<GraphValidationException>(() => RuleGraph.Build([
            Rule("core.build.compile-probe", "zzz.qqq.wwwwww"),
            Rule("core.workspace.toolchain"),
        ]));

        var error = exception.Errors.ShouldHaveSingleItem()
            .ShouldBeOfType<GraphValidationError.MissingDependency>();

        error.Suggestions.ShouldBeEmpty();
        error.Message.ShouldNotContain("Did you mean");
    }

    /// <remarks>
    /// This is the test that catches an implementation which filtered
    /// descriptors by the requested stage before building the graph — the
    /// mistake of filtering by stage first, relocated one layer down. Under it
    /// the candidate below would not be in the pool, and the suggestion would
    /// silently go missing.
    /// </remarks>
    [Fact]
    public void Build_SuggestsCandidatesFromAnyStage_NotOnlyTheDependentsOwnStage()
    {
        var exception = Should.Throw<GraphValidationException>(() => RuleGraph.Build([
            Rule("core.build.compile-probe", ValidationStage.BuildReadiness, "core.workspace.toolchian"),
            Rule("core.workspace.toolchain", ValidationStage.Workspace),
        ]));

        exception.Errors.ShouldHaveSingleItem()
            .ShouldBeOfType<GraphValidationError.MissingDependency>()
            .Suggestions.ShouldContain("core.workspace.toolchain");
    }

    [Fact]
    public void Build_WithTiedSuggestionDistance_ReturnsAllTiedCandidatesAlphabetically()
    {
        var exception = Should.Throw<GraphValidationException>(() => RuleGraph.Build([
            Rule("core.a.alpha", "core.a.rule"),
            Rule("core.a.rule-a"),
            Rule("core.a.rule-b"),
        ]));

        exception.Errors.ShouldHaveSingleItem()
            .ShouldBeOfType<GraphValidationError.MissingDependency>()
            .Suggestions.ShouldBe(["core.a.rule-a", "core.a.rule-b"]);
    }

    [Fact]
    public void Build_WithMultipleMissingDependencies_AccumulatesAllOfThem()
    {
        var exception = Should.Throw<GraphValidationException>(() => RuleGraph.Build([
            Rule("core.a.alpha", "core.a.nowhere"),
            Rule("core.z.yankee", "core.z.nothing"),
        ]));

        exception.Errors.Count.ShouldBe(2);
        exception.Errors.ShouldAllBe(error => error is GraphValidationError.MissingDependency);
    }
}
