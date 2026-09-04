namespace Preflight.Core.Graph;

/// <summary>
/// Thrown when <see cref="RuleGraph.Build"/> finds one or more defects.
/// </summary>
/// <remarks>
/// Accumulates every problem in the descriptor set rather than stopping at the
/// first, matching what policy validation already does: someone fixing a rule
/// set should see everything wrong with it in one pass.
/// </remarks>
public sealed class GraphValidationException : ConfigurationLoadException
{
    public GraphValidationException(IReadOnlyList<GraphValidationError> errors)
        : base(string.Join(Environment.NewLine, errors.Select(error => error.Message)))
    {
        Errors = errors;
    }

    public IReadOnlyList<GraphValidationError> Errors { get; }
}
