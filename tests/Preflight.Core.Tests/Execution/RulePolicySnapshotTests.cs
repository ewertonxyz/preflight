namespace Preflight.Core.Tests.Execution;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core;
using static Preflight.Core.Tests.Graph.GraphFixture;

/// <summary>
/// Fixes the values the engine freezes before running a rule.
/// </summary>
/// <remarks>
/// execution requires <c>EffectiveSeverity</c>, <c>Blocking</c>
/// and <c>Gating</c> to be <em>recorded</em> on the execution, not merely
/// consulted during it: a <c>report</c> over thirty days of history cannot
/// answer "was this rule blocking when it failed?" once the policy has changed.
/// Taking the snapshot in its own type is what lets that be tested without
/// running anything.
/// </remarks>
public sealed class RulePolicySnapshotTests
{
    private static readonly RuleDescriptor Descriptor = Rule("core.presubmit.large-file");

    [Fact]
    public void For_WithNoPolicyOverrides_MirrorsTheDescriptorDefaults()
    {
        var descriptor = Descriptor with
        {
            DefaultSeverity = Severity.Warning,
            DefaultBlocking = false,
            DefaultGating = false,
            DefaultTimeoutSeconds = 45,
        };

        var snapshot = RulePolicySnapshot.For(descriptor.Id, PolicyFixture.For().Build([descriptor]));

        snapshot.EffectiveSeverity.ShouldBe(Severity.Warning);
        snapshot.Blocking.ShouldBeFalse();
        snapshot.Gating.ShouldBeFalse();
        snapshot.Enabled.ShouldBeTrue();
        snapshot.Timeout.ShouldBe(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void For_WithEveryKeyOverridden_MirrorsThePolicy()
    {
        var policy = PolicyFixture.For()
            .Rule(Descriptor.Id.Value, blocking: false, gating: false, severity: "information", timeoutSeconds: 30)
            .Build([Descriptor]);

        var snapshot = RulePolicySnapshot.For(Descriptor.Id, policy);

        snapshot.EffectiveSeverity.ShouldBe(Severity.Information);
        snapshot.Blocking.ShouldBeFalse();
        snapshot.Gating.ShouldBeFalse();
        snapshot.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
    }

    /// <remarks>
    /// The second row is the one that matters: the root cascade of the policy schema
    /// already resolves a rule's timeout when the rule itself is silent, and the
    /// snapshot must read that result rather than re-derive it. A second cascade
    /// here would be a second answer, free to drift from the first.
    /// </remarks>
    [Theory]
    [InlineData(30L, null, 30)]
    [InlineData(null, 45L, 45)]
    public void For_ConvertsTimeoutSecondsIntoATimeSpan(long? ruleTimeout, long? rootDefault, int expectedSeconds)
    {
        var policy = PolicyFixture.For()
            .Rule(Descriptor.Id.Value, timeoutSeconds: ruleTimeout)
            .Root(defaultTimeoutSeconds: rootDefault)
            .Build([Descriptor]);

        RulePolicySnapshot.For(Descriptor.Id, policy).Timeout.ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void For_WithADisabledRule_ReportsItAsDisabled()
    {
        var policy = PolicyFixture.For().Rule(Descriptor.Id.Value, enabled: false).Build([Descriptor]);

        RulePolicySnapshot.For(Descriptor.Id, policy).Enabled.ShouldBeFalse();
    }

    /// <remarks>
    /// A rule without a snapshot has no recorded <c>Blocking</c>, and a verdict
    /// computed from a missing value is a verdict that changed for a reason
    /// nobody can see.
    /// </remarks>
    [Fact]
    public void ForAll_ReturnsOneSnapshotPerRuleId()
    {
        var descriptors = new[] { Rule("core.a.alpha"), Rule("core.a.bravo") };
        var policy = PolicyFixture.For().Build(descriptors);

        var snapshots = RulePolicySnapshot.ForAll(descriptors.Select(d => d.Id), policy);

        snapshots.Keys.Select(id => id.Value).OrderBy(value => value, StringComparer.Ordinal)
            .ShouldBe(["core.a.alpha", "core.a.bravo"]);
    }
}
