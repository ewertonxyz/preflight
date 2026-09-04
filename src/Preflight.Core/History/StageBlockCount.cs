namespace Preflight.Core.History;

using Preflight.Abstractions.Model;

/// <summary>How many runs one stage blocked.</summary>
/// <param name="Stage">The stage.</param>
/// <param name="Count">How many of its runs ended <c>Blocked</c> on their own merits.</param>
public sealed record StageBlockCount(ValidationStage Stage, int Count);
