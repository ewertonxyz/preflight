namespace Preflight.Core.Tests;

using System.Reflection;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Fixes the four factory methods on <see cref="RuleOutcome"/>, the two
/// factories that were deliberately left out, and documents the gap between
/// that omission and what the type still allows.
/// </summary>
/// <remarks>
/// There are deliberately no <c>Skipped()</c> or <c>Errored()</c> factories:
/// those two statuses are produced by the engine, not by a rule. But <c>Status</c> is a public
/// <c>init</c> property with no restriction, so the object initializer can
/// still construct either status directly. The last test below pins that as
/// current behaviour rather than something this phase corrects — changing it
/// now would be exactly the kind of surface change the plugin version contract prices as
/// expensive once a plugin depends on the current shape.
/// </remarks>
public sealed class RuleOutcomeTests
{
    [Fact]
    public void Passed_ProducesPassedStatusWithNoFindings()
    {
        var outcome = RuleOutcome.Passed();

        outcome.Status.ShouldBe(RuleStatus.Passed);
        outcome.Findings.ShouldBeEmpty();
    }

    [Fact]
    public void NotApplicable_ProducesNotApplicableStatusWithNoFindings()
    {
        var outcome = RuleOutcome.NotApplicable();

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
        outcome.Findings.ShouldBeEmpty();
    }

    [Fact]
    public void Warned_ProducesWarningStatusCarryingTheGivenFindingsInOrder()
    {
        var first = new Finding { Message = "first" };
        var second = new Finding { Message = "second" };
        var third = new Finding { Message = "third" };

        var outcome = RuleOutcome.Warned(first, second, third);

        outcome.Status.ShouldBe(RuleStatus.Warning);
        outcome.Findings.ShouldBe([first, second, third]);
    }

    [Fact]
    public void Failed_ProducesFailedStatusCarryingTheGivenFindingsInOrder()
    {
        var first = new Finding { Message = "first" };
        var second = new Finding { Message = "second" };

        var outcome = RuleOutcome.Failed(first, second);

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldBe([first, second]);
    }

    [Fact]
    public void RuleOutcome_DoesNotExposeSkippedOrErroredFactoryMethods()
    {
        var staticMethodNames = typeof(RuleOutcome)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.Name)
            .ToArray();

        staticMethodNames.ShouldNotContain("Skipped");
        staticMethodNames.ShouldNotContain("Errored");
    }

    [Fact]
    public void RuleOutcome_StatusInitSetterAllowsConstructingEngineOnlyStatusesDirectly()
    {
        var skipped = new RuleOutcome { Status = RuleStatus.Skipped };
        var errored = new RuleOutcome { Status = RuleStatus.Errored };

        skipped.Status.ShouldBe(RuleStatus.Skipped);
        errored.Status.ShouldBe(RuleStatus.Errored);
    }

    /// <remarks>
    /// The factories copy what they are handed. Without the copy,
    /// <c>Findings</c> would be a view onto an array the caller still holds,
    /// and the read-only list type would be promising something it does not
    /// deliver — a rule refilling one buffer per iteration would rewrite
    /// outcomes it had already returned, and the report would show the last
    /// finding several times over.
    /// </remarks>
    [Theory]
    [InlineData(RuleStatus.Warning)]
    [InlineData(RuleStatus.Failed)]
    public void Factories_WhenTheCallerMutatesTheArrayItPassed_DoNotChangeTheOutcome(RuleStatus status)
    {
        var buffer = new[] { new Finding { Message = "first" } };

        var outcome = status is RuleStatus.Warning
            ? RuleOutcome.Warned(buffer)
            : RuleOutcome.Failed(buffer);

        buffer[0] = new Finding { Message = "overwritten" };

        outcome.Findings.ShouldHaveSingleItem().Message.ShouldBe("first");
    }

    [Fact]
    public void RuleOutcome_TwoInstancesWithDifferentFindingsArrayInstances_AreNotEqual()
    {
        var finding = new Finding { Message = "first" };

        var first = RuleOutcome.Warned(finding);
        var second = RuleOutcome.Warned(finding);

        first.ShouldNotBe(second);
    }
}
