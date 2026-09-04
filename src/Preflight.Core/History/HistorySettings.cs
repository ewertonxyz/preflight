namespace Preflight.Core.History;

using Preflight.Core.Policy;

/// <summary>
/// The two root policy keys that decide where the history goes and how it is
/// laid out.
/// </summary>
/// <remarks>
/// Both keys already exist in <see cref="PolicyKeySchema"/> and already have
/// tool defaults, because the schema declared them before the history
/// existed. This type is the one place that reads them, so a command never
/// spells the key name itself.
/// </remarks>
/// <param name="Path">
/// <c>historyPath</c>, as configured: relative to the workspace root unless it
/// is rooted.
/// </param>
/// <param name="Mode"><c>historyMode</c>.</param>
public sealed record HistorySettings(string Path, HistoryMode Mode)
{
    /// <summary>The <c>historyPath</c> policy key.</summary>
    public const string PathKey = "historyPath";

    /// <summary>The <c>historyMode</c> policy key.</summary>
    public const string ModeKey = "historyMode";

    /// <summary>
    /// Reads both keys out of a resolved policy.
    /// </summary>
    /// <remarks>
    /// The fallback to <see cref="HistoryMode.Shared"/> is defence in depth,
    /// not a second default: <c>PolicyValidator</c> refuses any other value at
    /// load time, so the only way to reach it is a policy built without
    /// validation. It falls back rather than throwing because a mode nobody can
    /// spell is not a reason to lose the run's record, and <c>shared</c> is
    /// what the schema documents as the default.
    /// </remarks>
    public static HistorySettings From(EffectivePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        return new HistorySettings(
            policy.RootValue<string>(PathKey).Value,
            HistoryModeParser.Parse(policy.RootValue<string>(ModeKey).Value) ?? HistoryMode.Shared);
    }
}
