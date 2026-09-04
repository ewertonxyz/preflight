namespace Preflight.Core.Execution;

using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;

/// <summary>
/// Supplies each rule the logger it is handed in its context.
/// </summary>
/// <remarks>
/// The logger given to a rule arrives already scoped to that rule's id and
/// serialises its writes, so two rules running in parallel never interleave a
/// line. Both are tool behaviour, and the tool cannot provide either
/// without knowing which rule is asking — hence a factory rather than a single
/// logger.
///
/// It lives in <c>Preflight.Core</c>, not in <c>Abstractions</c>: a rule never
/// sees it, only the <see cref="IRuleLogger"/> it returns. The concrete sink is
/// injected by the CLI, because the tool knows nothing about how output is
/// formatted — that separation is what lets the same run be printed to a
/// console, to JSON and to SARIF without the tool gaining a third opinion.
/// </remarks>
public interface IRuleLoggerFactory
{
    IRuleLogger ForRule(RuleId ruleId);
}
