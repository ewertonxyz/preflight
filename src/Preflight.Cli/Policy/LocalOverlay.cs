namespace Preflight.Cli.Policy;

using Preflight.Cli.Services;

/// <summary>
/// Decides whether the local overlay participates, per the local overlay rules.
/// </summary>
/// <remarks>
/// The rule this encodes is one line of the design and one integrity hole in
/// practice: <c>preflight.local.json</c> is unversioned and nothing stops a
/// <c>"blocking": false</c> from surviving in it. Trusting nobody to forget
/// "the kind of thing that works until gold week".
/// </remarks>
public static class LocalOverlay
{
    /// <summary>
    /// The variables whose presence means CI, in a fixed order.
    /// </summary>
    /// <remarks>
    /// The order is not decoration. When two of them are set — <c>CI</c> and
    /// <c>GITHUB_ACTIONS</c> together is the common case, not the exotic one —
    /// <c>explain</c> prints <c>CI detected: &lt;which&gt;</c>, and an
    /// enumeration order that varies makes that line stop being diffable
    /// intermittently.
    /// </remarks>
    public static readonly IReadOnlyList<string> CiVariables =
    [
        "CI",
        "TEAMCITY_VERSION",
        "GITHUB_ACTIONS",
        "BUILD_BUILDID",
        "JENKINS_URL",
    ];

    /// <summary>
    /// The first CI variable that is present and non-empty, or
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Present <em>and non-empty</em>. The consequence is worth stating because
    /// it reads backwards: <c>CI=false</c> is a present, non-empty value, so it
    /// means CI. Every automation server that sets these sets them to something
    /// truthy, and a variable set to the string "false" is far more likely to
    /// be a leftover than an intention.
    /// </remarks>
    public static string? DetectCi(IEnvironmentReader environment)
    {
        foreach (var name in CiVariables)
        {
            if (!string.IsNullOrEmpty(environment.GetVariable(name)))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>
    /// Applies the four-row table above.
    /// </summary>
    /// <param name="environment">Where CI variables are read from.</param>
    /// <param name="noLocal"><c>--no-local</c> was passed.</param>
    /// <param name="allowLocal"><c>--allow-local</c> was passed.</param>
    /// <param name="fileExists">A <c>preflight.local.json</c> is on disk.</param>
    /// <remarks>
    /// <c>--no-local</c> is checked before <c>--allow-local</c>, and the two
    /// together never reach here: the parser rejects that combination with exit
    /// 2, because they are separate rows of that table and it defines no
    /// precedence between them. Deciding integrity by flag order would be a
    /// decision nobody made.
    /// </remarks>
    public static LocalOverlayDecision Decide(
        IEnvironmentReader environment,
        bool noLocal,
        bool allowLocal,
        bool fileExists)
    {
        var ciVariable = DetectCi(environment);

        if (noLocal)
        {
            return new LocalOverlayDecision(false, ciVariable, LocalOverlaySuppression.ExplicitlyDisabled);
        }

        if (ciVariable is not null && !allowLocal)
        {
            return new LocalOverlayDecision(false, ciVariable, LocalOverlaySuppression.CiDetected);
        }

        return fileExists
            ? new LocalOverlayDecision(true, ciVariable, LocalOverlaySuppression.None)
            : new LocalOverlayDecision(false, ciVariable, LocalOverlaySuppression.FileAbsent);
    }
}
