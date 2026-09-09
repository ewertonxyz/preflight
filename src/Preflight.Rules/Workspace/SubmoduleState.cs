namespace Preflight.Rules;

/// <summary>
/// What the leading character of a submodule status line says.
/// </summary>
/// <remarks>
/// Named states rather than the raw character, so that the rule deciding what
/// to report never repeats the client's alphabet. Adding a state the client
/// starts emitting is one row here and one arm in the rule, and neither is a
/// character literal buried in a comparison.
/// </remarks>
internal enum SubmoduleState
{
    /// <summary>The recorded commit is checked out.</summary>
    UpToDate,

    /// <summary>The submodule has never been initialised.</summary>
    Uninitialised,

    /// <summary>What is checked out is not what the index records.</summary>
    Diverged,

    /// <summary>A merge inside the submodule was never resolved.</summary>
    Conflicted,

    /// <summary>
    /// A leading character this tool does not know.
    /// </summary>
    /// <remarks>
    /// Reported rather than skipped. A line quietly ignored is a submodule
    /// nobody checked inside a run that reported success, which is the failure
    /// the whole rule exists to prevent.
    /// </remarks>
    Unrecognised,
}
