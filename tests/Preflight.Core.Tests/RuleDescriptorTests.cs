namespace Preflight.Core.Tests;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Fixes the defaults of <see cref="RuleDescriptor"/> and pins the one place
/// its record-generated equality does not do what it looks like it does.
/// </summary>
/// <remarks>
/// the rule descriptor: every <c>Default</c>-prefixed member is only a
/// default — policy has the final word (policy). The rule descriptor also has an
/// equality trap: <c>DependsOn</c> is an <c>IReadOnlyList&lt;RuleId&gt;</c>, and
/// a record's generated <c>Equals</c> compares that property with
/// <c>EqualityComparer&lt;IReadOnlyList&lt;RuleId&gt;&gt;.Default</c>, which is
/// reference equality for an ordinary list or array — not a sequence
/// comparison.
/// </remarks>
public sealed class RuleDescriptorTests
{
    [Fact]
    public void RuleDescriptor_WhenOnlyRequiredMembersAreSet_AppliesTheDocumentedDefaults()
    {
        var descriptor = new RuleDescriptor
        {
            Id = new RuleId("core.presubmit.large-file"),
            DisplayName = "Large file",
            Stage = ValidationStage.PreSubmit,
        };

        descriptor.DependsOn.ShouldBeEmpty();
        descriptor.DefaultSeverity.ShouldBe(Severity.Error);
        descriptor.DefaultBlocking.ShouldBeTrue();
        descriptor.DefaultGating.ShouldBeTrue();
        descriptor.DefaultTimeoutSeconds.ShouldBe(60);
        descriptor.Documentation.ShouldBeNull();
    }

    [Fact]
    public void RuleDescriptor_TwoInstancesWithDifferentDependsOnListInstances_AreNotEqual()
    {
        var dependency = new RuleId("core.presubmit.other-rule");

        var first = DescriptorDependingOn([dependency]);
        var second = DescriptorDependingOn([dependency]);

        first.ShouldNotBe(second);
    }

    private static RuleDescriptor DescriptorDependingOn(IReadOnlyList<RuleId> dependsOn) =>
        new()
        {
            Id = new RuleId("core.presubmit.large-file"),
            DisplayName = "Large file",
            Stage = ValidationStage.PreSubmit,
            DependsOn = dependsOn,
        };
}
