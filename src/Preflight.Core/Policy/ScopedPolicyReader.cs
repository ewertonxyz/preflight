namespace Preflight.Core.Policy;

using System.Diagnostics.CodeAnalysis;
using Preflight.Abstractions.Services;

/// <summary>
/// The <see cref="IPolicyReader"/> handed to a single rule.
/// </summary>
/// <remarks>
/// Scoped to exactly one rule's <c>settings</c> object. This type only ever
/// sees that subtree — obtained via
/// <see cref="EffectivePolicy.ReaderFor"/> — so root keys, engine fields, and
/// other rules' settings are not merely hidden by convention, there is no path
/// from here that reaches them.
///
/// <paramref name="key"/> accepts a dotted path (e.g. <c>"limits.maxBytes"</c>)
/// to reach a nested settings value, matching the same dotted-path convention
/// <c>--set</c> already uses for <c>settings.*</c>.
/// </remarks>
public sealed class ScopedPolicyReader : IPolicyReader
{
    private readonly PolicyNode.ObjectNode _settings;

    public ScopedPolicyReader(PolicyNode.ObjectNode settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
    }

    public T GetValue<T>(string key, T fallback) =>
        TryGetValue<T>(key, out var value) ? value : fallback;

    // The guard is on TryGetValue alone. GetValue reaches it on both of its
    // paths, so a second one here would repeat a check the call below already
    // makes, on the hottest read in the engine.

    public bool TryGetValue<T>(string key, [MaybeNullWhen(false)] out T value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!_settings.TryGetPath(key, out var node) || node is not PolicyNode.Leaf leaf)
        {
            value = default;
            return false;
        }

        value = PolicyValueConversion.Convert<T>(leaf.Value.Value);
        return true;
    }
}
