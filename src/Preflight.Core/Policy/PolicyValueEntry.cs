namespace Preflight.Core.Policy;

/// <summary>
/// One value a policy layer contributed, and where it came from.
/// </summary>
public sealed record PolicyValueEntry<T>(T Value, PolicyOrigin Origin);
