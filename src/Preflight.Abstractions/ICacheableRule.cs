namespace Preflight.Abstractions;

/// <summary>
/// A rule that can describe its own inputs, and may therefore be cached.
/// </summary>
/// <remarks>
/// <para>
/// A separate, optional interface rather than a member on
/// <see cref="IValidationRule"/>, and the versioning contract is the whole reason: adding a
/// member to the interface every plugin implements is a <b>major</b> version
/// and forces every plugin to be recompiled. A new type is a minor one. A rule
/// that does not implement this is never cached and does not change by one
/// character.
/// </para>
/// <para>
/// It also keeps a simple rule simple. Somebody checking a file size should not
/// have to understand a cache in order to write a rule, which is what a default
/// interface implementation on <c>IValidationRule</c> would have cost.
/// </para>
/// </remarks>
public interface ICacheableRule
{
    /// <summary>
    /// Describes everything this rule will read, or declines to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returning <see langword="null"/> means "I cannot describe my inputs in
    /// this run", and the result is that nothing is cached.
    /// <b>There is no approximate fingerprint.</b> A key that errs by optimism
    /// produces a <c>Passed</c> over a workspace that changed — the false green
    /// of principle 7, in its most expensive form to diagnose, because the
    /// evidence of the mistake is precisely the run that did not happen.
    /// </para>
    /// <para>
    /// A rule that reads the clock, the network or a service cannot enumerate
    /// its inputs and must return <see langword="null"/>.
    /// </para>
    /// </remarks>
    /// <param name="context">The same context the rule will be executed with.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<CacheFingerprint?> ComputeFingerprintAsync(
        RuleContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// An opaque description of one rule's inputs.
/// </summary>
/// <remarks>
/// The engine never inspects the value; it only compares it. What goes in it is
/// the rule's own business. The engine does not know what a rule reads, and an
/// engine-inferred fingerprint was rejected for exactly that reason: it could
/// only err by optimism, and optimism here is a cached pass over a workspace
/// that changed.
/// </remarks>
/// <param name="Value">Whatever identifies this set of inputs.</param>
public readonly record struct CacheFingerprint(string Value);
