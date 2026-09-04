namespace Preflight.Cli.Parsing;

using System.Globalization;
using Preflight.Abstractions.Rules;
using Preflight.Core;
using Preflight.Core.Policy;

/// <summary>
/// Parses a <c>--set</c> argument into the already-typed
/// <see cref="PolicySetOverride"/> the engine merges.
/// </summary>
/// <remarks>
/// Two forms are accepted and one of them exists only as a convenience:
/// <code>
/// --set core.presubmit.large-file:settings.maxBytes=1024   explicit
/// --set :maxDegreeOfParallelism=4                          root key
/// --set core.presubmit.large-file.blocking=false           greedy
/// </code>
/// The colon exists because rule ids contain dots, so without it nothing can
/// tell where the id ends and the key path begins. The greedy form resolves
/// against the discovered ids, and when more than one matches as a prefix it
/// fails naming both candidates rather than picking one silently — a silent
/// pick would apply an override to a rule the user did not name.
/// </remarks>
public static class SetOverrideParser
{
    private const string ExpectedForm = "<rule-id>:<key>=<value>";

    /// <summary>
    /// Parses one <c>--set</c> argument.
    /// </summary>
    /// <param name="argument">The raw text after <c>--set</c>.</param>
    /// <param name="knownRuleIds">
    /// The ids discovered for this run. Used to resolve the greedy form and to
    /// reject an id nothing answers to, with a suggestion.
    /// </param>
    /// <exception cref="SetOverrideParseException">The argument is malformed.</exception>
    public static PolicySetOverride Parse(string argument, IReadOnlyList<RuleId> knownRuleIds)
    {
        ArgumentNullException.ThrowIfNull(argument);
        ArgumentNullException.ThrowIfNull(knownRuleIds);

        var equals = argument.IndexOf('=', StringComparison.Ordinal);

        if (equals < 0)
        {
            throw new SetOverrideParseException(
                $"'--set {argument}' is missing '='. Expected '{ExpectedForm}'.");
        }

        // First '=', not the only one. A settings value is free text and may
        // contain '=' of its own; splitting on the last would silently move part
        // of the value into the key.
        var target = argument[..equals];
        var rawValue = argument[(equals + 1)..];

        var (ruleId, path) = SplitTarget(argument, target, knownRuleIds);

        if (path.Length == 0)
        {
            throw new SetOverrideParseException(
                $"'--set {argument}' names no key. Expected '{ExpectedForm}'.");
        }

        return new PolicySetOverride
        {
            RuleId = ruleId,
            Path = path,
            TypedValue = TypeValue(rawValue),
        };
    }

    /// <summary>
    /// Types the raw text, in this order.
    /// </summary>
    /// <remarks>
    /// The quoted form is checked first even though the table lists it last,
    /// because it is the escape hatch for the rows above it: its whole purpose
    /// is to stop <c>"true"</c> from becoming a boolean. Checked last, it could
    /// never do that.
    /// </remarks>
    private static object? TypeValue(string raw)
    {
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
        {
            return raw[1..^1];
        }

        if (string.Equals(raw, "true", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(raw, "false", StringComparison.Ordinal))
        {
            return false;
        }

        // long, not int, and a value too large for long falls through to string
        // rather than throwing. PolicyValidator then reports it as the wrong
        // type for the key, which is a message about the value the user typed —
        // an overflow here would be a message about this parser.
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        if (raw.Length >= 2 && raw[0] == '[' && raw[^1] == ']')
        {
            var inner = raw[1..^1];

            return inner.Length == 0
                ? Array.Empty<string>()
                : inner.Split(',').Select(element => element.Trim()).ToArray();
        }

        return raw;
    }

    private static (RuleId? RuleId, string Path) SplitTarget(
        string argument,
        string target,
        IReadOnlyList<RuleId> knownRuleIds)
    {
        var colon = target.IndexOf(':', StringComparison.Ordinal);

        if (colon < 0)
        {
            return ResolveGreedily(argument, target, knownRuleIds);
        }

        var idText = target[..colon];
        var path = target[(colon + 1)..];

        // An empty id is not an omission — it has a meaning:
        // ':maxDegreeOfParallelism=4' targets a root key.
        return idText.Length == 0
            ? (null, path)
            : (RequireKnownRuleId(idText, knownRuleIds), path);
    }

    /// <summary>
    /// Resolves the colon-less convenience form against the discovered ids.
    /// </summary>
    private static (RuleId? RuleId, string Path) ResolveGreedily(
        string argument,
        string target,
        IReadOnlyList<RuleId> knownRuleIds)
    {
        var candidates = knownRuleIds
            .Where(id => target.StartsWith(id.Value + ".", StringComparison.Ordinal))
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray();

        if (candidates.Length > 1)
        {
            throw new SetOverrideParseException(
                $"'--set {argument}' is ambiguous: it could name " +
                string.Join(" or ", candidates.Select(id => $"'{id.Value}'")) +
                $". Disambiguate with the ':' form, for example '{candidates[0].Value}:<key>=<value>'.");
        }

        if (candidates.Length == 0)
        {
            // Nothing matched as a prefix. The most useful reading is that the
            // user meant a rule id and mistyped it, so the whole target is
            // offered to the suggester rather than reported as a shapeless
            // parse failure.
            throw UnknownRuleId(target, knownRuleIds);
        }

        var id = candidates[0];

        return (id, target[(id.Value.Length + 1)..]);
    }

    private static RuleId RequireKnownRuleId(string idText, IReadOnlyList<RuleId> knownRuleIds)
    {
        RuleId ruleId;

        try
        {
            ruleId = new RuleId(idText);
        }
        catch (ArgumentException exception)
        {
            // RuleId validates in its constructor and throws. Left
            // uncaught, 'preflight run --set Core.Foo:blocking=false' would exit
            // 3 with a stack trace — an internal error, for a typo the user can
            // fix. Caught here it is exit 2 with the message RuleId already
            // wrote, which names the expected shape.
            throw new SetOverrideParseException(exception.Message);
        }

        return knownRuleIds.Contains(ruleId) ? ruleId : throw UnknownRuleId(idText, knownRuleIds);
    }

    private static SetOverrideParseException UnknownRuleId(string idText, IReadOnlyList<RuleId> knownRuleIds)
    {
        var suggestions = SuggestionFinder.FindClosest(idText, knownRuleIds.Select(id => id.Value));

        var message = $"No rule with id '{idText}' was discovered.";

        return new SetOverrideParseException(
            suggestions.Count == 0
                ? message
                : $"{message} Did you mean {string.Join(" or ", suggestions.Select(id => $"'{id}'"))}?");
    }
}
