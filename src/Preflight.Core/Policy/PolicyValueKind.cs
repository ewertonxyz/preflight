namespace Preflight.Core.Policy;

/// <summary>
/// What kind of value a policy key is allowed to hold.
/// </summary>
internal enum PolicyValueKind
{
    Boolean,
    Integer,
    String,

    /// <summary>A string restricted to a closed set, listed in the key's definition.</summary>
    StringEnum,

    /// <summary>An object whose members are themselves validated — today only <c>rules</c>.</summary>
    RuleMap,

    /// <summary>An object whose contents are never inspected — today only <c>settings</c>.</summary>
    Opaque,

    /// <summary>
    /// An object keyed by <c>PolicyTargetKey</c>, whose members are validated
    /// as root scopes — today only <c>targets</c>.
    /// </summary>
    TargetMap,

    /// <summary>An array of strings — today only <c>sealed</c>.</summary>
    StringArray,

    /// <summary>
    /// An object holding an inclusive minimum and an optional exclusive
    /// maximum — today only <c>requiresPipeline</c>.
    /// </summary>
    /// <remarks>
    /// A kind of its own rather than <see cref="Opaque"/>, because the whole
    /// point of the key is to be checked: a range whose members were never
    /// inspected would let a typo turn a bound into no bound, and the checkout
    /// would stop requiring anything with nobody told. See ADR-032.
    /// </remarks>
    VersionRange,
}
