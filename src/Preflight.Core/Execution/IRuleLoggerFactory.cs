namespace Preflight.Core;

using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;

/// <summary>
/// Supplies each rule the logger it is handed in its context.
/// </summary>
/// <remarks>
/// The logger given to a rule arrives already scoped to that rule's id and
/// serialises its writes, so two rules running in parallel never interleave a
/// line. Both are engine behaviour, and the engine cannot provide either
/// without knowing which rule is asking — hence a factory rather than a single
/// logger.
///
/// It lives in <c>Preflight.Core</c>, not in <c>Abstractions</c>: a rule never
/// sees it, only the <see cref="IRuleLogger"/> it returns. The concrete sink is
/// injected by the CLI, because 4.2 keeps Core from knowing anything about how
/// output is formatted.
/// </remarks>
public interface IRuleLoggerFactory
{
    IRuleLogger ForRule(RuleId ruleId);
}
