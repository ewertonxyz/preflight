namespace Preflight.Cli.Policy;

/// <summary>
/// Why the local overlay was left out.
/// </summary>
public enum LocalOverlaySuppression
{
    /// <summary>It was not left out.</summary>
    None,

    /// <summary>A CI environment was detected.</summary>
    CiDetected,

    /// <summary><c>--no-local</c> was passed.</summary>
    ExplicitlyDisabled,

    /// <summary>There is no <c>preflight.local.json</c> to apply.</summary>
    FileAbsent,
}
