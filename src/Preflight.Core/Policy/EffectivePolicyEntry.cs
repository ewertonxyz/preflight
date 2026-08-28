namespace Preflight.Core.Policy;

/// <summary>
/// One row of the effective-policy table <c>preflight explain</c> prints: a
/// key, its resolved value, and the full history of layers that produced it.
/// </summary>
/// <remarks>
/// The value is kept as <see cref="PolicyValue{T}"/> of <see cref="object"/>
/// rather than converted to a target type, because the caller enumerating this
/// list does not know what type each key holds — that is the whole reason the
/// list exists. Rendering a value for display is a different job from reading
/// one for use, and only the second one needs a type.
/// </remarks>
/// <param name="Key">
/// Dot-separated and relative to the rule: <c>blocking</c>, or
/// <c>settings.maxBytes</c>.
/// </param>
/// <param name="Value">
/// The resolved value and every layer that contributed, weakest first — which
/// is what the <c>overrides</c> line of <c>explain</c> is read from.
/// </param>
public sealed record EffectivePolicyEntry(string Key, PolicyValue<object?> Value);
