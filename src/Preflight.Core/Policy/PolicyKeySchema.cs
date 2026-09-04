namespace Preflight.Core.Policy;

using System.Collections.Frozen;

/// <summary>
/// The single declarative description of every key a policy file may contain,
/// per scope.
/// </summary>
/// <remarks>
/// <para>
/// Strict about everything except the contents of <c>settings</c>. That
/// strictness is two separate questions the schema keeps together — is the key
/// name allowed at all, and is its value the right shape — so that
/// <see cref="PolicyValidator"/> can answer both with one walk per scope
/// instead of one hand-written validator per scope.
/// </para>
/// <para>
/// Keeping root keys and rule keys in one table matters more than it looks. Two
/// hand-written allow-lists drift: a key added to one and forgotten in the
/// other still passes that scope's own tests, and the gap only surfaces on a
/// real policy file. Here, adding a key is one row, and the "did you mean"
/// candidates for a typo come from the same rows.
/// </para>
/// <para>
/// <see cref="FrozenDictionary{TKey, TValue}"/> rather than a plain dictionary
/// because these are built once at type-load and then only read — which is
/// precisely the trade it optimises for, at no cost in dependencies since it
/// ships in the BCL.
/// </para>
/// </remarks>
internal static class PolicyKeySchema
{
    /// <summary>
    /// Declared before the tables on purpose: static field initialisers run in
    /// declaration order, so a range declared after them would still be null
    /// when they are built, and every bound would silently vanish.
    /// </summary>
    private static readonly PolicyValueRange PositiveInt32 = new(1, int.MaxValue);

    public static readonly FrozenDictionary<string, PolicyKeyDefinition> RootKeys = ToTable([
        new("schemaVersion", PolicyValueKind.Integer),
        new("extends", PolicyValueKind.String),
        new("pipeline", PolicyValueKind.String),

        // The former spelling of "pipeline", still accepted. Removing it would
        // turn every policy file written before the key was renamed into a
        // load-time error, under a schema that refuses every key it does not
        // list — which is a migration and not a rename. Declaring both together
        // is refused by PolicyValidator.
        new("production", PolicyValueKind.String),
        new("maxDegreeOfParallelism", PolicyValueKind.Integer, Range: PositiveInt32),
        new("defaultTimeoutSeconds", PolicyValueKind.Integer, Range: PositiveInt32),
        new("historyPath", PolicyValueKind.String),
        new("historyMode", PolicyValueKind.StringEnum, ["shared", "per-process"]),
        new("cachePath", PolicyValueKind.String),
        new("rules", PolicyValueKind.RuleMap),

        // Between the pipeline document and the local overlay in the
        // precedence chain. Declared here rather than tolerated, like every
        // other key: the table is the one place a key exists.
        new("targets", PolicyValueKind.TargetMap),

        // The paths a descendant may not override. Unioned along the extends
        // chain, never replaced.
        new(PolicySeal.KeyName, PolicyValueKind.StringArray),

        // The range of pipeline package versions this checkout accepts. The
        // name is spelled out rather than taken from Preflight.Cli, which is
        // downstream of here and which a test forbids this project from
        // referencing; the constant on that side names this one back.
        new(RequiresPipelineKeyName, PolicyValueKind.VersionRange),
    ]);

    /// <summary>The root key holding a pipeline package version range.</summary>
    public const string RequiresPipelineKeyName = "requiresPipeline";

    /// <summary>
    /// The members a <see cref="PolicyValueKind.VersionRange"/> object may hold.
    /// </summary>
    /// <remarks>
    /// A table like every other scope, so an unknown member inside the range
    /// gets the same "did you mean" treatment as an unknown key at the root.
    /// Both are strings here and not integers: a version is three components,
    /// and parsing it is the reader's job, done where the error can say what a
    /// version looks like.
    /// </remarks>
    public static readonly FrozenDictionary<string, PolicyKeyDefinition> VersionRangeKeys = ToTable([
        new("minimumVersion", PolicyValueKind.String),
        new("maximumVersion", PolicyValueKind.String),
    ]);

    /// <summary>
    /// The rule keys in the order <c>preflight explain</c> prints them, which
    /// is also the order they are declared in.
    /// </summary>
    /// <remarks>
    /// <see cref="FrozenDictionary{TKey, TValue}"/> does not preserve insertion
    /// order, and the explain table needs one. The ordered list is the source
    /// and <see cref="RuleKeys"/> is built from it, rather than the two being
    /// written out separately — a second hand-written list is the same drift
    /// this whole table exists to avoid, with the difference that it would fail
    /// as a reordered report rather than as a rejected key.
    /// </remarks>
    public static readonly IReadOnlyList<PolicyKeyDefinition> RuleKeyOrder =
    [
        new("enabled", PolicyValueKind.Boolean),
        new("blocking", PolicyValueKind.Boolean),
        new("gating", PolicyValueKind.Boolean),
        new("severity", PolicyValueKind.StringEnum, ["information", "warning", "error"]),
        new("timeoutSeconds", PolicyValueKind.Integer, Range: PositiveInt32),
        new("settings", PolicyValueKind.Opaque),
    ];

    public static readonly FrozenDictionary<string, PolicyKeyDefinition> RuleKeys = ToTable(RuleKeyOrder);

    private static FrozenDictionary<string, PolicyKeyDefinition> ToTable(IEnumerable<PolicyKeyDefinition> definitions) =>
        definitions.ToFrozenDictionary(definition => definition.Name, StringComparer.Ordinal);
}
