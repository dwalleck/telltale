using Telltale.Pipeline.Tests.Harness;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>Pins what the harness clock does, because retry waits in the pipeline tests rely on it.</summary>
public class SkippingClockTests
{
    private static readonly DateTimeOffset Start = Local(2026, 2, 3, 12);

    [Test]
    public async Task A_delay_is_complete_at_once_is_recorded_and_moves_now_by_exactly_the_delay()
    {
        var clock = new SkippingClock(Start);

        var delay = Task.Delay(TimeSpan.FromSeconds(30), clock);

        // Already complete when Task.Delay returns: no real time is involved, so nothing here can be slow or flaky.
        await Assert.That(delay.IsCompletedSuccessfully).IsTrue();
        await Assert.That(clock.Waits).IsEquivalentTo(new[] { TimeSpan.FromSeconds(30) });
        await Assert.That(clock.GetUtcNow()).IsEqualTo(Start.AddSeconds(30));
    }

    [Test]
    public async Task Two_delays_in_a_row_are_recorded_in_order_and_add_up()
    {
        var clock = new SkippingClock(Start);

        await Task.Delay(TimeSpan.FromSeconds(30), clock);
        await Task.Delay(TimeSpan.FromSeconds(7), clock);

        await Assert.That(clock.Waits).IsEquivalentTo(new[] { TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(7) }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(clock.GetUtcNow()).IsEqualTo(Start.AddSeconds(37));
    }

    [Test]
    public async Task Reading_the_time_does_not_move_it()
    {
        var clock = new SkippingClock(Start);

        await Assert.That(clock.GetUtcNow()).IsEqualTo(Start);
        await Assert.That(clock.GetUtcNow()).IsEqualTo(Start);
        await Assert.That(clock.Waits).IsEmpty();
    }

    [Test]
    public async Task A_timeout_built_on_the_clock_fires_at_once_which_is_why_none_may_be()
    {
        // Documented limit: every timer fires immediately. Code under test must not build timeouts on the injected clock.
        var clock = new SkippingClock(Start);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5), clock);

        await Assert.That(timeout.IsCancellationRequested).IsTrue();
        await Assert.That(clock.GetUtcNow()).IsEqualTo(Start.AddMinutes(5));
    }
}
