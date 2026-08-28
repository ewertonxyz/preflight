namespace Preflight.Core.Caching;

using System.Text.Json;
using System.Text.Json.Serialization;
using Preflight.Abstractions;

/// <summary>
/// What one cached result looks like on disk.
/// </summary>
/// <remarks>
/// <para>
/// An explicit record rather than serialising <see cref="RuleOutcome"/>
/// directly. The outcome is a published contract with factory methods and a
/// deliberately awkward <c>init</c> setter (see IDEAS.md); pointing a
/// deserialiser at it would make every future change to that type a change to a
/// file format, silently.
/// </para>
/// <para>
/// Enums are written by name, for the same reason the history does it: a
/// consumer reading an ordinal has to keep a copy of the declaration order, and
/// inserting a value into the enum would change the meaning of every entry
/// already on disk. Here the consumer is this same program a week later, which
/// makes it worse rather than better.
/// </para>
/// </remarks>
public static class CachedOutcomeDocument
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The stored shape.</summary>
    /// <param name="Status">What the rule concluded.</param>
    /// <param name="Findings">The evidence it gave.</param>
    public sealed record Stored(RuleStatus Status, IReadOnlyList<Finding> Findings);

    public static string Serialise(RuleOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return JsonSerializer.Serialize(new Stored(outcome.Status, outcome.Findings), Options);
    }

    /// <summary>
    /// The outcome <paramref name="json"/> holds, or <see langword="null"/>
    /// when it holds nothing usable.
    /// </summary>
    /// <remarks>
    /// A cache entry that cannot be read is a miss, never an error. It is a
    /// file this program wrote for its own convenience, and refusing to
    /// validate a workspace because of one would be the instrumentation
    /// deciding it outranks the function — the same subordination the history
    /// observes.
    /// </remarks>
    public static RuleOutcome? Deserialise(string json)
    {
        Stored? stored;

        try
        {
            stored = JsonSerializer.Deserialize<Stored>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }

        if (stored is null)
        {
            return null;
        }

        return new RuleOutcome { Status = stored.Status, Findings = stored.Findings ?? [] };
    }
}
