namespace Preflight.Core.Policy;

/// <summary>
/// One problem found while validating a policy load. <see cref="JsonPath"/> is
/// for display, not for reparsing — a rule id already contains dots, so a naive
/// dotted path built from it is ambiguous to walk back programmatically, which
/// is also why <c>--set</c> has a <c>:</c> separator.
/// </summary>
public sealed record PolicyValidationError(string Message, string? FilePath, int? Line, string? JsonPath);

/// <summary>
/// Thrown once a policy load's validation accumulates one or more
/// <see cref="PolicyValidationError"/>s.
/// </summary>
/// <remarks>
/// The user chose accumulation over fail-fast: every problem found across every
/// document in the load is reported together, not just the first one.
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
