namespace Preflight.Core.Tests.Execution;

using Preflight.Abstractions;
using Preflight.Core;
using static Preflight.Core.Tests.Graph.GraphFixture;

/// <summary>
/// Fixes the skip propagation of skip attribution: who is skipped,
/// why, and — the part the design cares most about — which rule is named as the
/// cause.
/// </summary>
/// <remarks>
/// <para>
/// Attribution points at the original failure, never at the immediate parent.
/// The design shows both formats side by side and says the wrong one "manda o
/// desenvolvedor investigar o lugar errado".
/// </para>
/// <para>
/// When several terminal ancestors reach the same node, all are listed,
/// shallowest first — shallowest meaning lowest topological level, because that
/// is the one most likely to be the real cause. The alternative reading,
/// distance in edges from the skipped node, produces exactly the
/// immediate-parent attribution this exists to avoid.
/// </para>
/// <para>
/// Pure: no async, no scheduling, no clock. Every case here would otherwise
/// have to be provoked through a parallel run, trading an eight-line test for a
/// forty-line one with a race in it.
/// </para>
/// </remarks>
public sealed class SkipPropagationTests
{
    [Fact]
    public void Compute_WithAGatingFailure_SkipsEveryTransitiveDependent()
    {
        var descriptors = Chain3();
        var attributions = Compute(descriptors, ("core.a.charlie", RuleStatus.Failed), gating: true);

        attributions.Keys.Select(id => id.Value).OrderBy(value => value, StringComparer.Ordinal)
            .ShouldBe(["core.a.alpha", "core.a.bravo"]);
    }

    /// <remarks>
    /// <c>blocking</c> and <c>gating</c> are independent axes. A rule
    /// can fail the run without invalidating what depends on it.
    /// </remarks>
    [Fact]
    public void Compute_WithANonGatingFailure_SkipsNobody()
    {
        var descriptors = Chain3();

        Compute(descriptors, ("core.a.charlie", RuleStatus.Failed), gating: false).ShouldBeEmpty();
    }

    /// <remarks>
    /// The worked example of skip attribution, with the design's own rule ids. Every
    /// dependent names <c>core.workspace.toolchain</c>; none names
    /// <c>core.build.configuration</c>, which is the format the design rejects.
    /// </remarks>
    [Fact]
    public void Compute_AttributesTheSkipToTheOriginalFailureNotTheImmediateParent()
    {
        var descriptors = new[]
        {
            Rule("core.workspace.toolchain"),
            Rule("core.workspace.dependencies", "core.workspace.toolchain"),
            Rule("core.build.configuration", "core.workspace.toolchain"),
            Rule("core.build.compile-probe", "core.build.configuration"),
        };

        var attributions = Compute(descriptors, ("core.workspace.toolchain", RuleStatus.Failed), gating: true);

        attributions.Count.ShouldBe(3);

        foreach (var attribution in attributions.Values)
        {
            attribution.SkippedBecauseOf.Select(id => id.Value).ShouldBe(["core.workspace.toolchain"]);
        }
    }

    [Theory]
    [InlineData(RuleStatus.Failed, SkipReason.DependencyFailed)]
    [InlineData(RuleStatus.Errored, SkipReason.DependencyErrored)]
    public void Compute_WithATerminalAncestor_UsesTheMatchingSkipReason(RuleStatus status, SkipReason expected)
    {
        var attributions = Compute(Chain3(), ("core.a.charlie", status), gating: true);

        attributions[new RuleId("core.a.bravo")].Reason.ShouldBe(expected);
    }

    /// <remarks>
    /// Neither of these is a terminal failure. Skipping on a warning would take
    /// half a run off the board because something said "careful"; skipping on
    /// not-applicable would gut a pre-submit run whose diff happened to be
    /// empty, and then report it as passed.
    /// </remarks>
    [Theory]
    [InlineData(RuleStatus.Warning)]
    [InlineData(RuleStatus.NotApplicable)]
    [InlineData(RuleStatus.Passed)]
    public void Compute_WithANonTerminalStatus_SkipsNobody(RuleStatus status)
    {
        Compute(Chain3(), ("core.a.charlie", status), gating: true).ShouldBeEmpty();
    }

    /// <remarks>The half of the ordering that carries the advice.</remarks>
    [Fact]
    public void Compute_WithTwoTerminalAncestorsInDifferentLevels_OrdersThemShallowestFirst()
    {
        // zulu sits at level 0, mid at level 1, and the ids are chosen so that
        // ordinal order would put them the other way round.
        var descriptors = new[]
        {
            Rule("core.a.zulu"),
            Rule("core.a.mid", "core.a.zulu"),
            Rule("core.a.leaf", "core.a.mid"),
        };

        var attributions = Compute(
            descriptors,
            ("core.a.zulu", RuleStatus.Failed),
            ("core.a.mid", RuleStatus.Failed));

        attributions[new RuleId("core.a.leaf")].SkippedBecauseOf.Select(id => id.Value)
            .ShouldBe(["core.a.zulu", "core.a.mid"]);
    }

    /// <remarks>
    /// The other half of the ordering. Without an ordinal tie-break the order of
    /// equally deep ancestors comes out of a hash set, and the report stops
    /// being diffable intermittently — the worst way for it to stop.
    /// </remarks>
    [Fact]
    public void Compute_WithTwoTerminalAncestorsInTheSameLevel_BreaksTheTieByRuleIdOrdinal()
    {
        var descriptors = new[]
        {
            Rule("core.a.rule-2"),
            Rule("core.a.rule-10"),
            Rule("core.a.leaf", "core.a.rule-2", "core.a.rule-10"),
        };

        var attributions = Compute(
            descriptors,
            ("core.a.rule-2", RuleStatus.Failed),
            ("core.a.rule-10", RuleStatus.Failed));

        attributions[new RuleId("core.a.leaf")].SkippedBecauseOf.Select(id => id.Value)
            .ShouldBe(["core.a.rule-10", "core.a.rule-2"]);
    }

    /// <remarks>
    /// Skip attribution gives one <c>SkipReason</c> and a list of causes. When the
    /// causes disagree, the reason follows the shallowest one, so that the
    /// reason and the first name in the list tell the same story.
    /// </remarks>
    [Fact]
    public void Compute_WithAncestorsOfDifferentKinds_TakesTheReasonOfTheShallowest()
    {
        var descriptors = new[]
        {
            Rule("core.a.zulu"),
            Rule("core.a.mid", "core.a.zulu"),
            Rule("core.a.leaf", "core.a.mid"),
        };

        var attributions = Compute(
            descriptors,
            ("core.a.zulu", RuleStatus.Errored),
            ("core.a.mid", RuleStatus.Failed));

        var attribution = attributions[new RuleId("core.a.leaf")];

        attribution.SkippedBecauseOf[0].Value.ShouldBe("core.a.zulu");
        attribution.Reason.ShouldBe(SkipReason.DependencyErrored);
    }

    /// <remarks>
    /// Handoff from the graph: <c>ExecutionSet</c> speaks its own vocabulary for a
    /// disabled dependency, and this is where it becomes the report's.
    /// </remarks>
    [Fact]
    public void Compute_WithADisabledDependency_UsesDependencyDisabled()
    {
        var descriptors = Chain3();
        var graph = RuleGraph.Build(descriptors);

        var attributions = SkipPropagation.Compute(
            graph,
            terminalStatuses: new Dictionary<RuleId, RuleStatus>(),
            snapshots: Snapshots(descriptors, gating: true),
            disabled:
            [
                new ExecutionSet.SkippedByDisabledDependency
                {
                    RuleId = new RuleId("core.a.bravo"),
                    DisabledDependencies = [new RuleId("core.a.charlie")],
                },
            ],
            noSkip: false);

        var attribution = attributions[new RuleId("core.a.bravo")];

        attribution.Reason.ShouldBe(SkipReason.DependencyDisabled);
        attribution.SkippedBecauseOf.Select(id => id.Value).ShouldBe(["core.a.charlie"]);
    }

    [Fact]
    public void Compute_WithNoSkipRequested_LeavesGatingDependentsRunnable()
    {
        var descriptors = Chain3();
        var graph = RuleGraph.Build(descriptors);

        var attributions = SkipPropagation.Compute(
            graph,
            new Dictionary<RuleId, RuleStatus> { [new RuleId("core.a.charlie")] = RuleStatus.Failed },
            Snapshots(descriptors, gating: true),
            disabled: [],
            noSkip: true);

        attributions.ShouldBeEmpty();
    }

    /// <remarks>
    /// The one thing <c>--no-skip</c> does not resurrect. A disabled dependency
    /// literally did not run, so running its dependent would be validating
    /// against a prerequisite that was never established — which is a different
    /// thing from seeing the full picture, and the flag exists for the second.
    /// </remarks>
    [Fact]
    public void Compute_WithNoSkipRequested_StillSkipsForADisabledDependency()
    {
        var descriptors = Chain3();
        var graph = RuleGraph.Build(descriptors);

        var attributions = SkipPropagation.Compute(
            graph,
            terminalStatuses: new Dictionary<RuleId, RuleStatus>(),
            Snapshots(descriptors, gating: true),
            disabled:
            [
                new ExecutionSet.SkippedByDisabledDependency
                {
                    RuleId = new RuleId("core.a.bravo"),
                    DisabledDependencies = [new RuleId("core.a.charlie")],
                },
            ],
            noSkip: true);

        attributions.ShouldContainKey(new RuleId("core.a.bravo"));
    }

    [Fact]
    public void Compute_WithNoTerminalFailures_ReturnsEmpty()
    {
        Compute(Chain3()).ShouldBeEmpty();
    }

    private static RuleDescriptor[] Chain3() =>
    [
        Rule("core.a.alpha", "core.a.bravo"),
        Rule("core.a.bravo", "core.a.charlie"),
        Rule("core.a.charlie"),
    ];

    private static IReadOnlyDictionary<RuleId, SkipPropagation.SkipAttribution> Compute(
        IReadOnlyList<RuleDescriptor> descriptors,
        params (string Id, RuleStatus Status)[] terminals) =>
        Compute(descriptors, gating: true, terminals);

    private static IReadOnlyDictionary<RuleId, SkipPropagation.SkipAttribution> Compute(
        IReadOnlyList<RuleDescriptor> descriptors,
        (string Id, RuleStatus Status) terminal,
        bool gating) =>
        Compute(descriptors, gating, [terminal]);

    private static IReadOnlyDictionary<RuleId, SkipPropagation.SkipAttribution> Compute(
        IReadOnlyList<RuleDescriptor> descriptors,
        bool gating,
        (string Id, RuleStatus Status)[] terminals) =>
        SkipPropagation.Compute(
            RuleGraph.Build(descriptors),
            terminals.ToDictionary(entry => new RuleId(entry.Id), entry => entry.Status),
            Snapshots(descriptors, gating),
            disabled: [],
            noSkip: false);

    private static Dictionary<RuleId, RulePolicySnapshot> Snapshots(
        IReadOnlyList<RuleDescriptor> descriptors, bool gating) =>
        descriptors.ToDictionary(
            descriptor => descriptor.Id,
            descriptor => new RulePolicySnapshot
            {
                RuleId = descriptor.Id,
                Enabled = true,
                Blocking = true,
                Gating = gating,
                EffectiveSeverity = Severity.Error,
                Timeout = TimeSpan.FromSeconds(60),
            });
}
