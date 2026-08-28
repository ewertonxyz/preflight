namespace Preflight.Core.Caching;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core.Policy;

/// <summary>
/// The incremental cache, from the engine's side.
/// </summary>
/// <remarks>
/// <para>
/// Everything here fails soft. A fingerprint that throws, an entry that will
/// not parse, a directory that cannot be written: each of them means "no
/// cache", and none of them means "the run is wrong". A cache that can turn a
/// valid workspace into a failed run has made the tool less trustworthy in
/// exchange for speed, which is the wrong trade in a tool whose entire value is
/// being believed.
/// </para>
/// <para>
/// The one thing it does <b>not</b> do softly is guess. There is no approximate
/// fingerprint, so every path that cannot produce an exact key produces no key
/// at all.
/// </para>
/// </remarks>
public sealed class RuleCache
{
    private readonly IRuleCacheStore _store;
    private readonly string _directory;
    private readonly EffectivePolicy _policy;

    public RuleCache(IRuleCacheStore store, string directory, EffectivePolicy policy)
    {
        _store = store;
        _directory = directory;
        _policy = policy;
    }

    /// <summary>
    /// Whether an outcome may be stored at all.
    /// </summary>
    /// <remarks>
    /// A rule that exploded has to explode again. Caching a crash hides an
    /// unstable environment and turns an intermittent problem into a permanent,
    /// wrong result — and <c>Skipped</c> is produced by the engine rather than
    /// by a rule, so an entry claiming one was not written by this code and is
    /// not to be trusted.
    /// </remarks>
    public static bool IsCacheable(RuleOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return outcome.Status is not (RuleStatus.Errored or RuleStatus.Skipped);
    }

    /// <summary>
    /// Empties the cache. <c>preflight cache clear</c>.
    /// </summary>
    /// <returns>How many entries were removed.</returns>
    /// <exception cref="UnsafeCachePathException">
    /// The configured path is one this command refuses to empty.
    /// </exception>
    public int Clear(DirectoryInfo workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);

        RequireSafeToEmpty(workspaceRoot, _directory);

        return _store.Clear(_directory);
    }

    /// <summary>
    /// Refuses to empty a directory that is not exclusively the cache's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>cachePath</c> is a free string in a policy file, and any overlay may
    /// set it. Without this, <c>"cachePath": "."</c> turns
    /// <c>preflight cache clear</c> into a command that deletes every JSON file
    /// in the repository, recursively — a validation tool destroying the
    /// workspace it exists to protect, which is the worst outcome anything in
    /// this design could produce.
    /// </para>
    /// <para>
    /// The check is on the resolved path, not on the string, so <c>"a/.."</c>
    /// and <c>"."</c> are the same refusal. It refuses the workspace root and
    /// anything above it, and it refuses a path that would take the history
    /// down with it — the two directories are siblings under <c>.preflight</c>
    /// by default, and losing the history is a real cost rather than an
    /// inconvenience.
    /// </para>
    /// </remarks>
    public static void RequireSafeToEmpty(DirectoryInfo workspaceRoot, string directory)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(directory);

        var resolved = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot.FullName));

        if (string.Equals(resolved, root, StringComparison.OrdinalIgnoreCase) ||
            root.StartsWith(resolved + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnsafeCachePathException(
                $"'{CacheSettings.PathKey}' resolves to '{resolved}', which contains the workspace. " +
                "Refusing to empty it: point it at a directory of its own.");
        }
    }

    /// <summary>
    /// The key for this rule in this run, or <see langword="null"/> when it
    /// cannot be cached.
    /// </summary>
    /// <remarks>
    /// Three ways to get nothing, and they are different facts worth keeping
    /// apart in the code even though the caller treats them the same: the rule
    /// does not implement the interface at all, the rule declined to describe
    /// its inputs this time, or the rule threw while trying. Only the third is
    /// worth telling anybody about.
    /// </remarks>
    public async Task<string?> KeyForAsync(
        IValidationRule rule,
        RuleContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(context);

        if (rule is not ICacheableRule cacheable)
        {
            return null;
        }

        CacheFingerprint? fingerprint;

        try
        {
            fingerprint = await cacheable.ComputeFingerprintAsync(context, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Not the rule's verdict. A fingerprint that throws says the cache
            // cannot be used, and failing the rule over it would let a defect in
            // an optimisation reject somebody's workspace. It is reported rather
            // than swallowed, because a rule whose fingerprint always throws is
            // paying the interface's cost for none of its benefit.
            context.Logger.Warn(
                $"The cache fingerprint could not be computed, so this rule will not be cached: {exception.Message}");

            return null;
        }

        if (fingerprint is not { } value)
        {
            return null;
        }

        // The stage and the target belong in the key even though neither
        // describes the workspace. A rule reads both from its context, so the same
        // sources at --platform ps5 can legitimately produce a different answer
        // from the same sources at win64 — and a rule author who forgets to fold
        // them into the fingerprint gets a wrong result rather than a lost hit.
        // The engine knows both for certain; the rule only might.
        //
        // The module id of the rule's own assembly closes the last gap, and it
        // is the one a plugin opens: a rule whose code changed is a different
        // rule, and nothing else in the key notices.
        return CachePaths.KeyFor(
            rule.Descriptor.Id,
            value,
            CachePaths.PolicyDigestFor(rule.Descriptor.Id, _policy),
            context.Stage,
            context.Target,
            CachePaths.AbstractionsGeneration,
            rule.GetType().Assembly.ManifestModule.ModuleVersionId);
    }

    /// <summary>
    /// The stored outcome for <paramref name="key"/>, or
    /// <see langword="null"/>.
    /// </summary>
    public async Task<RuleOutcome?> TryReadAsync(
        RuleId ruleId,
        string key,
        RuleContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? content;

        try
        {
            content = await _store.ReadAsync(CachePaths.FileFor(_directory, ruleId, key), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            context.Logger.Debug($"The cache could not be read: {exception.Message}");

            return null;
        }

        if (content is null)
        {
            return null;
        }

        var outcome = CachedOutcomeDocument.Deserialise(content);

        // An entry holding a status the cache never writes was not written by
        // this code. Treating it as a miss costs one execution; trusting it
        // could report a skip nobody attributed or a crash nobody had.
        return outcome is not null && IsCacheable(outcome) ? outcome : null;
    }

    /// <summary>
    /// Stores an outcome, if it may be stored.
    /// </summary>
    /// <remarks>
    /// A null outcome is accepted and stored as nothing. A rule that returns
    /// one is a contract violation the runner already reports; making the
    /// caller test for it here as well would put a second condition on the one
    /// path where both have to agree.
    /// </remarks>
    public async Task WriteAsync(
        RuleId ruleId,
        string key,
        RuleOutcome? outcome,
        RuleContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (outcome is null || !IsCacheable(outcome))
        {
            return;
        }

        try
        {
            await _store.WriteAsync(
                CachePaths.FileFor(_directory, ruleId, key),
                CachedOutcomeDocument.Serialise(outcome),
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            context.Logger.Debug($"The result could not be cached: {exception.Message}");
        }
    }
}
