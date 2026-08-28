namespace Preflight.Abstractions.Rules;

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
