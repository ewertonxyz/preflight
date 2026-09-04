namespace Preflight.Core.Policy;

/// <summary>
/// One problem found while validating a policy load. <see cref="JsonPath"/> is
/// for display, not for reparsing — a rule id already contains dots, so a naive
/// dotted path built from it is ambiguous to walk back programmatically, which
/// is also why <c>--set</c> has a <c>:</c> separator.
/// </summary>
public sealed record PolicyValidationError(string Message, string? FilePath, int? Line, string? JsonPath);
