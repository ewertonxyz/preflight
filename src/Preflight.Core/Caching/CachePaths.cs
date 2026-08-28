namespace Preflight.Core.Caching;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core.Plugins;
using Preflight.Core.Policy;


/// <summary>
/// Where one cached result lives, and what identifies it.
/// </summary>
/// <remarks>
/// Pure, and separate from anything that touches a disk, because the key is the
/// part of this feature that is worth being certain about. The key has six
/// components, omitting any one of them produces a bug, and each of them is
/// therefore something a test can vary on its own.
/// </remarks>
public static class CachePaths
{
    /// <summary>The extension a cached result is stored under.</summary>
    public const string Extension = ".json";

    /// <summary>The glob <c>preflight cache clear</c> removes.</summary>
    public const string SearchPattern = "*" + Extension;

    /// <summary>
    /// The generation of <c>Preflight.Abstractions</c> this engine was built
    /// against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One component of the cache key. A result serialised under an older
    /// contract is not readable under a new one, and reading it anyway would
    /// resurrect a shape that no longer means what it says.
    /// </para>
    /// <para>
    /// The generation rather than the major, and the difference is not
    /// cosmetic while the contract is below 1.0. The major is <c>0</c> for
    /// every 0.x release, so a key built from it would be identical across
    /// 0.1 and 0.2 — two contracts SemVer explicitly allows to be
    /// incompatible — and the cache would serve a pass computed under a shape
    /// that no longer exists. <see cref="AbstractionsCompatibility.GenerationOf"/>
    /// owns that rule, and the loader reads it from the same place, so the two
    /// cannot drift into disagreeing about which contracts are the same.
    /// </para>
    /// </remarks>
    public static string AbstractionsGeneration { get; } =
        AbstractionsCompatibility.GenerationOf(AbstractionsCompatibility.HostVersion);

    /// <summary>
    /// The directory the cache lives in.
    /// </summary>
    /// <remarks>
    /// Resolved exactly as <c>historyPath</c> is: relative to the workspace
    /// root unless it is rooted. The two keys are siblings in the schema, and
    /// behaving differently would be a trap.
    /// </remarks>
    public static string DirectoryFor(DirectoryInfo workspaceRoot, string cachePath)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(cachePath);

        return Path.IsPathRooted(cachePath)
            ? cachePath
            : Path.Combine(workspaceRoot.FullName, cachePath);
    }

    /// <summary>
    /// The file one result is stored in:
    /// <c>&lt;cache&gt;/&lt;rule-id&gt;/&lt;key&gt;.json</c>.
    /// </summary>
    public static string FileFor(string directory, RuleId ruleId, string key) =>
        Path.Combine(directory, ruleId.Value, key + Extension);

    /// <summary>
    /// The cache key, from its components.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The components are joined with a separator that cannot occur in any of
    /// them, so that two different sets of components cannot render to the same
    /// string. Concatenating them directly would let a rule id ending in a
    /// digit and a fingerprint starting with one collide with their neighbours
    /// — a hash of the wrong thing is indistinguishable from a hash of the
    /// right one, which is the whole difficulty of debugging a cache.
    /// </para>
    /// <para>
    /// SHA-256 and not a faster non-cryptographic hash. This is not about
    /// security: it is that a collision here silently serves one rule's result
    /// for another's inputs, and the cost of that is unbounded while the cost
    /// of the hash is microseconds next to a fifteen-second compile probe.
    /// </para>
    /// </remarks>
    /// <param name="ruleAssemblyId">
    /// The module version id of the assembly that declares the rule. Without
    /// it, a plugin rebuilt with different logic, over unchanged inputs and an
    /// unchanged policy, would otherwise be served its predecessor's verdict
    /// and told <c>(cached)</c> about it. A version number cannot carry this,
    /// because it depends on the plugin author remembering to raise one; the
    /// module id is written by the compiler, and a deterministic build — which
    /// <c>Directory.Build.props</c> turns on for exactly this family of reasons
    /// — produces the same id for the same sources, so an unchanged plugin
    /// keeps its hits.
    /// </param>
    public static string KeyFor(
        RuleId ruleId,
        CacheFingerprint fingerprint,
        string policyDigest,
        ValidationStage stage,
        BuildTarget target,
        string abstractionsGeneration,
        Guid ruleAssemblyId)
    {
        ArgumentNullException.ThrowIfNull(policyDigest);
        ArgumentNullException.ThrowIfNull(target);

        var components = string.Join(
            '\u001f',
            ruleId.Value,
            fingerprint.Value,
            policyDigest,
            stage.ToString(),
            target.Platform,
            target.Configuration,
            abstractionsGeneration,
            ruleAssemblyId.ToString("N", CultureInfo.InvariantCulture));

        return Digest(components);
    }

    /// <summary>
    /// A digest of one rule's effective policy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One component of the cache key, and the one whose absence is the least
    /// obvious bug: changing <c>maxBytes</c> has to invalidate the result even
    /// though not one byte of the workspace changed.
    /// </para>
    /// <para>
    /// Built from the effective <em>values</em> and never from their
    /// provenance. The same limit reached through a different overlay is the
    /// same configuration, and hashing the origin would throw away every hit on
    /// a machine that merely names its policy file differently.
    /// </para>
    /// <para>
    /// <see cref="EffectivePolicy.RuleEntries"/> already fixes the order for
    /// <c>preflight explain</c>, so this reuses it rather than walking the tree
    /// again. Two traversals of one structure diverge in silence.
    /// </para>
    /// </remarks>
    public static string PolicyDigestFor(RuleId ruleId, EffectivePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var builder = new StringBuilder();

        foreach (var entry in policy.RuleEntries(ruleId))
        {
            builder.Append(entry.Key).Append('\u001f')
                .Append(Render(entry.Value.Value)).Append('\u001e');
        }

        return Digest(builder.ToString());
    }

    /// <summary>
    /// One effective policy value, rendered so that two different values cannot
    /// render alike.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Invariant culture and a fixed rendering per kind, because a policy
    /// digest computed on a pt-BR machine has to equal the one computed in CI.
    /// This is the same reason the console reporter formats every number
    /// invariantly, applied where the consequence is a cache that never hits
    /// across machines rather than a report that looks odd.
    /// </para>
    /// <para>
    /// The array arm is not a nicety. <c>PolicyDocument</c> parses every JSON
    /// array in <c>settings</c> into an <c>object?[]</c>, and the first version
    /// of this method let the discard arm call <c>ToString()</c> on it — which
    /// returns <c>"System.Object[]"</c> for every array there has ever been.
    /// Two different lists of forbidden path patterns therefore produced the
    /// same digest, and changing the list did not invalidate the cache: a
    /// <c>Passed</c> over a workspace whose policy had changed, which is the
    /// exact failure a cache is most expensive to diagnose for. It was found by
    /// the test-planning pass, not by a test.
    /// </para>
    /// <para>
    /// Order inside an array is kept, because order is information —
    /// <c>["a","b"]</c> and <c>["b","a"]</c> are different configurations even
    /// when they behave alike today. The discard arm carries <c>string</c>, the
    /// last of the kinds <c>PolicyDocument.ParseRawValue</c> produces, so the
    /// set is exhaustive and nothing needs excluding from coverage.
    /// </para>
    /// </remarks>
    private static string Render(object? value) => value switch
    {
        null => "null",
        bool flag => flag ? "true" : "false",
        long number => number.ToString(CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        object?[] items => "[" + string.Join(',', items.Select(Render)) + "]",
        _ => (string)value,
    };

    private static string Digest(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
