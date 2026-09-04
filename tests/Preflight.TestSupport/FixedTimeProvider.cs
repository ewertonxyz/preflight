namespace Preflight.TestSupport;

/// <summary>
/// A clock that does not move unless it is told to.
/// </summary>
/// <remarks>
/// <para>
/// <c>CommandEnvironment</c> has taken a <see cref="TimeProvider"/> since the
/// beginning, with remarks saying it exists so the byte-identical guarantee is
/// testable — and every call site passed
/// <see cref="TimeProvider.System"/>, which is the same as not injecting one
/// at all. The history is what finally needed it: its file is named after a
/// month, and a test asserting that name against the real clock is a test that
/// changes its mind on the first of every month.
/// </para>
/// <para>
/// <see cref="GetTimestamp"/> is derived from the same instant rather than from
/// <see cref="System.Diagnostics.Stopwatch"/>, so <c>GetElapsedTime</c> measures
/// exactly what <see cref="Advance"/> was told to add. Without the override the
/// base class falls back to the real high-resolution counter, and a duration
/// assertion becomes a race against how fast the machine ran the test.
/// </para>
/// <para>
/// Timers are deliberately left to the base implementation, which uses a real
/// one. Rule timeouts are wall-clock deadlines and should stay that way; making
/// them virtual here would let a frozen clock hang a test that was waiting for
/// one.
/// </para>
/// </remarks>
public sealed class FixedTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    /// <summary>Ticks, so <c>GetElapsedTime</c> is exact rather than rounded.</summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => _now;

    public override long GetTimestamp() => _now.UtcTicks;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
