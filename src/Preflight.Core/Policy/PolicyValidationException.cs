namespace Preflight.Core.Policy;

/// <summary>
/// Thrown once a policy load's validation accumulates one or more
/// <see cref="PolicyValidationError"/>s.
/// </summary>
/// <remarks>
/// Accumulation over fail-fast: every problem found across every document in
/// the load is reported together, not just the first one. Somebody who
/// mistyped four keys should be told about four, not asked to run the tool
/// four times.
/// <see cref="PolicyValidator.ValidateAll"/> never throws this itself — it
/// returns the list, and the caller decides whether a non-empty list is fatal.
/// </remarks>
public sealed class PolicyValidationException : ConfigurationLoadException
{
    public PolicyValidationException(IReadOnlyList<PolicyValidationError> errors)
        : base(string.Join(Environment.NewLine, errors.Select(error => error.Message)))
    {
        Errors = errors;
    }

    public IReadOnlyList<PolicyValidationError> Errors { get; }
}
