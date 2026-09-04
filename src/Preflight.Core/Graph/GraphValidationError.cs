namespace Preflight.Core.Graph;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// One defect found while building the rule graph.
/// </summary>
/// <remarks>
/// Four shapes: three are found while building the graph, and the fourth is
/// what a cross-stage dependency looks like when its target does not exist.
/// Each carries its evidence structurally — the cycle as an ordered list of
/// ids, the missing target next to its suggestions — so a reporter can format
/// them without parsing prose back out of a message, the same reason a
/// <c>Finding</c> keeps <c>Expected</c> and <c>Actual</c> out of its text.
/// </remarks>
public abstract record GraphValidationError
{
    private GraphValidationError()
    {
    }

    public abstract string Message { get; }

    /// <summary>
    /// A cycle, carrying the whole path in walk order and closing on the id it
    /// started from.
    /// </summary>
    /// <remarks>
    /// "Cycle detected" is not an acceptable message: a seven-node cycle
    /// described that way costs whoever fixes it half an hour of reading
    /// descriptors to find the edge they have to cut.
    /// </remarks>
    public sealed record CycleDetected(IReadOnlyList<RuleId> Path) : GraphValidationError
    {
        public override string Message =>
            $"Dependency cycle detected: {string.Join(" -> ", Path)}.";
    }

    /// <summary>
    /// A rule that depends on itself.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="CycleDetected"/> rather than folded in as its
    /// one-node case, so that the message can say what this almost always is:
    /// the wrong id pasted into <c>DependsOn</c>. Folded in, it would be
    /// reported as a cycle of length one and read as something exotic.
    /// </remarks>
    public sealed record SelfDependency(RuleId RuleId) : GraphValidationError
    {
        public override string Message =>
            $"Rule '{RuleId}' depends on itself. This is usually the wrong id pasted into DependsOn.";
    }

    public sealed record MissingDependency(RuleId RuleId, RuleId MissingTarget, IReadOnlyList<string> Suggestions)
        : GraphValidationError
    {
        public override string Message
        {
            get
            {
                var message =
                    $"Rule '{RuleId}' depends on '{MissingTarget}', which does not exist among the discovered rules.";

                return Suggestions.Count == 0
                    ? message
                    : $"{message} Did you mean '{string.Join("' or '", Suggestions)}'?";
            }
        }
    }

    /// <summary>
    /// The same rule id declared twice among the discovered rules.
    /// </summary>
    /// <remarks>
    /// Reported rather than resolved by last-one-wins: an id indexed into a
    /// dictionary twice would drop a rule out of the graph entirely, and a rule
    /// that never runs and never reports is the worst possible outcome for a
    /// tool whose job is reporting.
    /// </remarks>
    public sealed record DuplicateRuleId(RuleId RuleId) : GraphValidationError
    {
        public override string Message =>
            $"Rule id '{RuleId}' is declared more than once among the discovered rules.";
    }
}
