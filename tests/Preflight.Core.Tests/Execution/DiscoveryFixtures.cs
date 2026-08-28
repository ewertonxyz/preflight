namespace Preflight.Core.Tests.Execution;

using Preflight.Abstractions;
using static Preflight.Core.Tests.Graph.GraphFixture;

/// <summary>
/// Types that discovery has to make a decision about, each deliberately shaped
/// wrong in one way.
/// </summary>
/// <remarks>
/// Kept in one file so the deliberate breakage is localised and named. They are
/// reached through <c>RuleDiscovery.FromTypes</c> with an explicit list, never
/// by scanning the test assembly — a scan would sweep up every fake in the
/// project, and these tests would then fail whenever an unrelated fixture was
/// added.
/// </remarks>
public static class DiscoveryFixtures
{
    internal abstract class AbstractRule : IValidationRule
    {
        public RuleDescriptor Descriptor => Rule("core.fixture.abstract");

        public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken) =>
            Task.FromResult(RuleOutcome.Passed());
    }

    internal interface IDerivedRuleInterface : IValidationRule;

    internal sealed class OpenGenericRule<T> : IValidationRule
    {
        public RuleDescriptor Descriptor => Rule("core.fixture.generic");

        public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken) =>
            Task.FromResult(RuleOutcome.Passed());
    }

    internal sealed class NotARule
    {
        public override string ToString() => nameof(NotARule);
    }

    public sealed class WellFormedRule : IValidationRule
    {
        public RuleDescriptor Descriptor => Rule("core.fixture.well-formed");

        public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken) =>
            Task.FromResult(RuleOutcome.Passed());
    }

    public sealed class SecondWellFormedRule : IValidationRule
    {
        public RuleDescriptor Descriptor => Rule("core.fixture.second");

        public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken) =>
            Task.FromResult(RuleOutcome.Passed());
    }

    public sealed class ConstructorTakesParametersRule : IValidationRule
    {
        public ConstructorTakesParametersRule(int unused) => Unused = unused;

        public int Unused { get; }

        public RuleDescriptor Descriptor => Rule("core.fixture.needs-argument");

        public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken) =>
            Task.FromResult(RuleOutcome.Passed());
    }

    public sealed class ThrowingConstructorRule : IValidationRule
    {
        public ThrowingConstructorRule() =>
            throw new InvalidOperationException("this rule refuses to be constructed");

        public RuleDescriptor Descriptor => Rule("core.fixture.throwing-ctor");

        public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken) =>
            Task.FromResult(RuleOutcome.Passed());
    }

    public sealed class DuplicateIdRuleA : IValidationRule
    {
        public RuleDescriptor Descriptor => Rule("core.fixture.duplicate");

        public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken) =>
            Task.FromResult(RuleOutcome.Passed());
    }

    public sealed class DuplicateIdRuleB : IValidationRule
    {
        public RuleDescriptor Descriptor => Rule("core.fixture.duplicate");

        public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken) =>
            Task.FromResult(RuleOutcome.Passed());
    }

    internal sealed class NonPublicRule : IValidationRule
    {
        public RuleDescriptor Descriptor => Rule("core.fixture.non-public");

        public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken) =>
            Task.FromResult(RuleOutcome.Passed());
    }
}
