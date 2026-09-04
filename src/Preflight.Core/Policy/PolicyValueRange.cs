namespace Preflight.Core.Policy;

/// <summary>
/// The inclusive range an <see cref="PolicyValueKind.Integer"/> key accepts.
/// </summary>
/// <remarks>
/// The bounds are derived from the types the values end up in, not from taste.
/// Below 1 is meaningless for all three numeric keys — a timeout of zero errors
/// every rule instantly, and <c>Parallel.ForEachAsync</c> throws on a degree of
/// zero. Above <see cref="int.MaxValue"/> the value survives the JSON reader as
/// a <see langword="long"/> and then overflows on the way to
/// <see cref="TimeSpan.FromSeconds(double)"/> or to an <see langword="int"/>
/// worker count — an exception raised in the middle of a run, which the one
/// rule about validation forbids: it happens at load, never during execution.
/// </remarks>
internal sealed record PolicyValueRange(long Minimum, long Maximum);
