using Microsoft.Extensions.Time.Testing;

namespace Telltale.Pipeline.Tests.Harness;

/// <summary>
/// A fixed clock whose timers fire at once and move the clock forward by exactly the time waited.
/// A retry delay costs no real time, and a test can read how long the code under test chose to wait.
/// </summary>
/// <remarks>
/// It is for waits only (<c>Task.Delay(wait, clock)</c>). Every timer fires inside <c>CreateTimer</c>, before the
/// caller holds it, and moves "now" by its due time. So a timeout built on this clock
/// (<c>new CancellationTokenSource(timeout, clock)</c>, <c>CancelAfter</c>, <c>PeriodicTimer</c>) fires at once and shifts
/// the as-of time: code under test must take timeouts from real time, never from the injected clock.
/// <c>SkippingClockTests</c> pins both behaviours.
/// </remarks>
public sealed class SkippingClock(DateTimeOffset start) : FakeTimeProvider(start)
{
    private readonly Lock _gate = new();
    private readonly List<TimeSpan> _waits = [];

    /// <summary>Every wait the code under test asked for, in order.</summary>
    public IReadOnlyList<TimeSpan> Waits
    {
        get
        {
            lock (_gate)
            {
                return [.. _waits];
            }
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        if (dueTime > TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
        {
            lock (_gate)
            {
                _waits.Add(dueTime);
            }

            Advance(dueTime);
        }

        return timer;
    }
}
