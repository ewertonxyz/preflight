namespace Preflight.Core.Caching;

using Preflight.Core.Policy;

/// <summary>
/// The root policy key that decides where the cache goes.
/// </summary>
/// <remarks>
/// The sibling of <c>HistorySettings</c>, and it exists for the same reason: so
/// that <c>"cachePath"</c> is spelled in one place rather than at every call
/// site that needs it. The schema lists the two keys together, and a project
/// where one of them has a reader and the other is a string literal in three
/// files is one refactor away from them disagreeing.
/// </remarks>
/// <param name="Path">
/// <c>cachePath</c>, as configured: relative to the workspace root unless it is
/// rooted.
/// </param>
public sealed record CacheSettings(string Path)
{
    /// <summary>The <c>cachePath</c> policy key.</summary>
    public const string PathKey = "cachePath";

    /// <summary>Reads the key out of a resolved policy.</summary>
    public static CacheSettings From(EffectivePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        return new CacheSettings(policy.RootValue<string>(PathKey).Value);
    }
}
