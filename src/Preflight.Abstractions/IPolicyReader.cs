namespace Preflight.Abstractions;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Read-only access to a single rule's own policy settings.
/// </summary>
/// <remarks>
/// Scoped to the rule's own <see cref="RuleId"/> and to its <c>settings</c>
/// object alone — a rule cannot read or change another rule's configuration,
/// nor any root key.
///
/// The <see cref="MaybeNullWhenAttribute"/> on <see cref="TryGetValue{T}"/> is
/// not cosmetic: without it, with nullable enabled and warnings as errors,
/// every caller would have to treat <c>value</c> as possibly null even on the
/// branch where the method returned <see langword="true"/>, pushing <c>!</c>
/// into every rule.
/// </remarks>
public interface IPolicyReader
{
    T GetValue<T>(string key, T fallback);

    bool TryGetValue<T>(string key, [MaybeNullWhen(false)] out T value);
}
