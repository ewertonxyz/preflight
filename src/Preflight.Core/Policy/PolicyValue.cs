namespace Preflight.Core.Policy;

/// <summary>
/// Non-generic factory so callers do not spell out the type argument that
/// <see cref="PolicyValue{T}"/>'s own constructor would otherwise require —
/// and so the factory is not a static member on a generic type (CA1000).
/// </summary>
public static class PolicyValue
{
    public static PolicyValue<T> Initial<T>(T value, PolicyOrigin origin) =>
        new() { Entries = [new PolicyValueEntry<T>(value, origin)] };
}

/// <summary>
/// An effective value together with the full chain of layers that ever touched
/// it, oldest first.
/// </summary>
/// <remarks>
/// The user chose full history over "just the immediate predecessor": every
/// layer that ever set this key is retrievable, not only the one it most
/// recently overrode. <see cref="Value"/> and <see cref="Origin"/> are the last
/// entry; <see cref="History"/> is everything before it.
/// </remarks>
public sealed record PolicyValue<T>
{
    public required IReadOnlyList<PolicyValueEntry<T>> Entries { get; init; }

    public T Value => Entries[^1].Value;

    public PolicyOrigin Origin => Entries[^1].Origin;

    public IReadOnlyList<PolicyValueEntry<T>> History =>
        Entries.Count > 1 ? [.. Entries.Take(Entries.Count - 1)] : [];

    public PolicyValue<T> OverriddenBy(T value, PolicyOrigin origin) =>
        new() { Entries = [.. Entries, new PolicyValueEntry<T>(value, origin)] };
}
