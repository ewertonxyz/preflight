namespace Preflight.Core.Tests.Execution;

using Preflight.Abstractions;
using Preflight.Core.Policy;

/// <summary>
/// Builds a real <see cref="EffectivePolicy"/> from per-rule overrides.
/// </summary>
/// <remarks>
/// Goes through <c>PolicyDocument.Parse</c> rather than constructing the policy
/// some shorter way, so that the executor tests exercise the same resolution
/// path a real run does — including the root timeout cascade of the policy schema,
/// which the executor must read rather than reimplement.
/// </remarks>
internal sealed class PolicyFixture
{
    private readonly Dictionary<string, List<string>> _ruleSettings = [];
    private readonly List<string> _rootSettings = [];

    public static PolicyFixture For() => new();

    public PolicyFixture Rule(
        string id,
        bool? enabled = null,
        bool? blocking = null,
        bool? gating = null,
        string? severity = null,
        long? timeoutSeconds = null)
    {
        var entries = _ruleSettings.TryGetValue(id, out var existing) ? existing : _ruleSettings[id] = [];

        if (enabled is { } e)
        {
            entries.Add($"\"enabled\": {(e ? "true" : "false")}");
        }

        if (blocking is { } b)
        {
            entries.Add($"\"blocking\": {(b ? "true" : "false")}");
        }

        if (gating is { } g)
        {
            entries.Add($"\"gating\": {(g ? "true" : "false")}");
        }

        if (severity is not null)
        {
            entries.Add($"\"severity\": \"{severity}\"");
        }

        if (timeoutSeconds is { } t)
        {
            entries.Add($"\"timeoutSeconds\": {t}");
        }

        return this;
    }

    public PolicyFixture Root(long? maxDegreeOfParallelism = null, long? defaultTimeoutSeconds = null)
    {
        if (maxDegreeOfParallelism is { } m)
        {
            _rootSettings.Add($"\"maxDegreeOfParallelism\": {m}");
        }

        if (defaultTimeoutSeconds is { } d)
        {
            _rootSettings.Add($"\"defaultTimeoutSeconds\": {d}");
        }

        return this;
    }

    public EffectivePolicy Build(IReadOnlyList<RuleDescriptor> descriptors)
    {
        if (_ruleSettings.Count == 0 && _rootSettings.Count == 0)
        {
            return EffectivePolicy.Build(descriptors, pipeline: null, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);
        }

        var rules = string.Join(",", _ruleSettings.Select(entry =>
            $"\"{entry.Key}\": {{ {string.Join(",", entry.Value)} }}"));

        var parts = new List<string> { "\"schemaVersion\": 1" };
        parts.AddRange(_rootSettings);

        if (_ruleSettings.Count > 0)
        {
            parts.Add($"\"rules\": {{ {rules} }}");
        }

        var production = PolicyDocument.Parse($"{{ {string.Join(",", parts)} }}", "atlas.json");

        return EffectivePolicy.Build(descriptors, production, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);
    }
}
