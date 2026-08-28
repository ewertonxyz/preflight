namespace Preflight.Core.Tests.Plugins;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// A rule that behaves, under an id no production uses.
/// </summary>
public sealed class SampleRule : IValidationRule
{
    /// <summary>The id both this and <see cref="CollidingRule"/> claim.</summary>
    public const string Id = "acme.content.thing";

    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = new RuleId(Id),
        DisplayName = "Sample",
        Stage = ValidationStage.PreSubmit,
    };

    public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken) =>
        Task.FromResult(RuleOutcome.Passed());
}

/// <summary>A second well-behaved rule, so ordering has something to order.</summary>
public sealed class AnotherSampleRule : IValidationRule
{
    public const string Id = "acme.content.another";

    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = new RuleId(Id),
        DisplayName = "Another sample",
        Stage = ValidationStage.PreSubmit,
    };

    public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken) =>
        Task.FromResult(RuleOutcome.Passed());
}

/// <summary>
/// A different type claiming <see cref="SampleRule"/>'s id.
/// </summary>
/// <remarks>
/// A distinct type rather than the same one loaded twice, because that is the
/// case that actually happens: two productions writing the same obvious id in
/// two plugins nobody compared.
/// </remarks>
public sealed class CollidingRule : IValidationRule
{
    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = new RuleId(SampleRule.Id),
        DisplayName = "Colliding",
        Stage = ValidationStage.PreSubmit,
    };

    public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken) =>
        Task.FromResult(RuleOutcome.Passed());
}

/// <summary>
/// A rule the engine cannot instantiate.
/// </summary>
/// <remarks>
/// The rule interface: the engine constructs rules with <c>Activator</c> and no
/// container, so a constructor with a parameter is a rule that can never run.
/// It is refused rather than skipped, because a type that wrote
/// <c>: IValidationRule</c> declared an intent the engine has to honour or
/// reject out loud.
/// </remarks>
public sealed class ConstructorArgumentRule : IValidationRule
{
    public ConstructorArgumentRule(string _)
    {
    }

    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = new RuleId("acme.content.constructor"),
        DisplayName = "Needs an argument",
        Stage = ValidationStage.PreSubmit,
    };

    public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken) =>
        Task.FromResult(RuleOutcome.Passed());
}

/// <summary>
/// A rule whose descriptor is computed, and throws.
/// </summary>
/// <remarks>
/// Legal C#, and what a plugin author writing
/// <c>public RuleDescriptor Descriptor =&gt; Build();</c> arrives at without
/// trying. Before the plugin loader this escaped discovery entirely and left the process
/// on exit 3 with a stack trace, claiming an internal error for a defect in
/// somebody's rule.
/// </remarks>
public sealed class ThrowingDescriptorRule : IValidationRule
{
    public RuleDescriptor Descriptor => throw new InvalidOperationException("no descriptor for you");

    public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken) =>
        Task.FromResult(RuleOutcome.Passed());
}

/// <summary>
/// A rule whose id does not survive <see cref="RuleId"/>'s constructor.
/// </summary>
/// <remarks>
/// The id is built in a field initialiser, so the failure happens while the
/// rule is being constructed rather than while its descriptor is read. Both
/// paths end at exit 2 naming the type, and they are different paths.
/// </remarks>
public sealed class InvalidRuleIdRule : IValidationRule
{
    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = new RuleId("Acme.Content.Thing"),
        DisplayName = "Shouty",
        Stage = ValidationStage.PreSubmit,
    };

    public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken) =>
        Task.FromResult(RuleOutcome.Passed());
}

/// <summary>Not a rule at all, and not a problem.</summary>
public sealed class HelperType
{
    public static string Describe() => "a helper a plugin depends on";
}
