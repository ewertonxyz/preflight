namespace Preflight.Core.History;

/// <summary>
/// Turns the <c>historyMode</c> policy value into the enum.
/// </summary>
/// <remarks>
/// <para>
/// A parser rather than a <c>switch</c> inside the writer, and the reason is
/// coverage rather than tidiness. <c>PolicyKeySchema</c> already closes this
/// key to two literals, so a <c>switch</c> in the writer would carry an arm no
/// input can reach — a permanent hole in the branch count, or a fabricated test
/// written to close it. Here the unknown arm is reachable by calling this
/// method, which is exactly what a test should be allowed to do.
/// </para>
/// <para>
/// <c>StageParser</c>, in the command line project, has the same shape for the
/// same reason. It is named rather than linked because the engine does not
/// reference that project and a link from here could never resolve.
/// </para>
/// </remarks>
public static class HistoryModeParser
{
    /// <summary>The <c>shared</c> policy literal.</summary>
    public const string SharedValue = "shared";

    /// <summary>The <c>per-process</c> policy literal.</summary>
    public const string PerProcessValue = "per-process";

    /// <summary>Every value the policy accepts, in a fixed order.</summary>
    public static IReadOnlyList<string> AcceptedValues { get; } = [SharedValue, PerProcessValue];

    /// <summary>
    /// The mode <paramref name="value"/> names, or <see langword="null"/> when
    /// it names none.
    /// </summary>
    public static HistoryMode? Parse(string? value) => value switch
    {
        SharedValue => HistoryMode.Shared,
        PerProcessValue => HistoryMode.PerProcess,
        _ => null,
    };
}
