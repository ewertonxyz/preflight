namespace Preflight.Cli;

using Preflight.Abstractions;

/// <summary>
/// Maps between the <c>--stage</c> spelling on the command line and
/// <see cref="ValidationStage"/>.
/// </summary>
/// <remarks>
/// Hand-written rather than <c>Enum.Parse</c>, because the command line spells
/// two of the three in kebab-case — <c>pre-submit</c>, <c>build-readiness</c> —
/// and no enum parser produces those. The mapping is one table, in one place,
/// so the spelling the user types and the spelling the help text lists cannot
/// drift apart.
/// </remarks>
public static class StageParser
{
    /// <summary>
    /// Every accepted <c>--stage</c> value, in a fixed order.
    /// </summary>
    /// <remarks>
    /// Ordered because it is printed: the help text and the error message for a
    /// bad value both enumerate it, and an order that varies between runs makes
    /// a golden file of either one fail intermittently.
    /// </remarks>
    public static readonly IReadOnlyList<string> AcceptedValues =
    [
        "workspace",
        "pre-submit",
        "build-readiness",
    ];

    /// <summary>
    /// The stage <paramref name="value"/> names, or <see langword="null"/> if
    /// it names none.
    /// </summary>
    public static ValidationStage? Parse(string? value) => value switch
    {
        "workspace" => ValidationStage.Workspace,
        "pre-submit" => ValidationStage.PreSubmit,
        "build-readiness" => ValidationStage.BuildReadiness,
        _ => null,
    };

    /// <summary>
    /// How <paramref name="stage"/> is spelled on the command line.
    /// </summary>
    public static string ToArgument(ValidationStage stage) => stage switch
    {
        ValidationStage.Workspace => "workspace",
        ValidationStage.PreSubmit => "pre-submit",
        ValidationStage.BuildReadiness => "build-readiness",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unmapped validation stage."),
    };
}
