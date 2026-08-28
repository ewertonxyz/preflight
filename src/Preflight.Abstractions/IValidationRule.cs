namespace Preflight.Abstractions;

/// <summary>
/// A single validation rule.
/// </summary>
/// <remarks>
/// A rule does not decide whether it blocks, its final severity, whether it
/// gates its dependents, or anything about other rules — it only reports
/// passed, warned, failed or not-applicable, with evidence.
///
/// A rule needs a public parameterless constructor; the engine discovers types
/// by reflection and instantiates with <c>Activator.CreateInstance</c>.
/// Services reach the rule through <see cref="RuleContext"/>, not through the
/// constructor, so that <c>Preflight.Abstractions</c> never depends on a
/// dependency-injection container.
/// </remarks>
public interface IValidationRule
{
    RuleDescriptor Descriptor { get; }

    Task<RuleOutcome> ExecuteAsync(
        RuleContext context,
        CancellationToken cancellationToken);
}
