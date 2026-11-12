using Telltale.Core.Intervals;
using Telltale.Core.Workflow;
using static Telltale.Core.Tests.TestTime;

namespace Telltale.Core.Tests;

/// <summary>November 2026 for reading the dates: the 2nd, 9th and 16th are Mondays.</summary>
public class CyclesTests
{
    private const string New = "1";
    private const string Development = "4";
    private const string Review = "5";
    private const string QE = "6";
    private const string Blocked = "7";
    private const string Done = "8";
    private const string Shipped = "9";

    private static readonly StatusRoles Roles = StatusRoles.Parse(new Dictionary<string, string>
    {
        [New] = "not started",
        [Development] = "work",
        [Review] = "work",
        [QE] = "work",
        [Blocked] = "blocked",
        [Done] = "done",
        [Shipped] = "done",
    });

    private static readonly DateTimeOffset Created = Local(2026, 11, 2, 9);

    [Test]
    public async Task A_cycle_runs_from_the_first_entry_into_a_work_status_to_the_delivery()
    {
        // Tuesday 09:00 to Thursday 15:00.
        var cycle = CycleOf(Done,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Development),
            new StatusChange("2", Local(2026, 11, 4, 9), Development, Review),
            new StatusChange("3", Local(2026, 11, 5, 15), Review, Done));

        await Assert.That(cycle).IsNotNull();
        await Assert.That(cycle!.Start).IsEqualTo(Local(2026, 11, 3, 9));
        await Assert.That(cycle.CalendarDays).IsEqualTo(2.25).Within(0.000001);
        await Assert.That(cycle.BusinessDays).IsEqualTo(2.25).Within(0.000001);
    }

    [Test]
    public async Task Time_in_blocked_before_any_work_status_does_not_start_the_cycle()
    {
        var cycle = CycleOf(Done,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Blocked),
            new StatusChange("2", Local(2026, 11, 4, 9), Blocked, Development),
            new StatusChange("3", Local(2026, 11, 5, 9), Development, Done));

        await Assert.That(cycle!.Start).IsEqualTo(Local(2026, 11, 4, 9));
        await Assert.That(cycle.CalendarDays).IsEqualTo(1.0).Within(0.000001);
    }

    [Test]
    public async Task Time_in_blocked_after_the_start_is_part_of_the_cycle()
    {
        var cycle = CycleOf(Done,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Development),
            new StatusChange("2", Local(2026, 11, 4, 9), Development, Blocked),
            new StatusChange("3", Local(2026, 11, 5, 9), Blocked, Development),
            new StatusChange("4", Local(2026, 11, 6, 9), Development, Done));

        await Assert.That(cycle!.Start).IsEqualTo(Local(2026, 11, 3, 9));
        await Assert.That(cycle.CalendarDays).IsEqualTo(3.0).Within(0.000001);
        await Assert.That(cycle.BusinessDays).IsEqualTo(3.0).Within(0.000001);
    }

    [Test]
    [Arguments(Review)]
    [Arguments(QE)]
    public async Task An_issue_that_skips_development_starts_at_its_first_other_work_status(string firstWorkStatus)
    {
        var cycle = CycleOf(Done,
            new StatusChange("1", Local(2026, 11, 3, 9), New, firstWorkStatus),
            new StatusChange("2", Local(2026, 11, 4, 9), firstWorkStatus, Development),
            new StatusChange("3", Local(2026, 11, 5, 9), Development, Done));

        await Assert.That(cycle!.Start).IsEqualTo(Local(2026, 11, 3, 9));
    }

    [Test]
    public async Task An_issue_created_in_a_work_status_starts_its_cycle_when_it_was_created()
    {
        var cycle = CycleOf(Done,
            new StatusChange("1", Local(2026, 11, 4, 9), Development, Done));

        await Assert.That(cycle!.Start).IsEqualTo(Created);
        await Assert.That(cycle.CalendarDays).IsEqualTo(2.0).Within(0.000001);
    }

    [Test]
    public async Task A_reopened_issues_cycle_runs_from_its_first_work_status_to_its_last_delivery()
    {
        var cycle = CycleOf(Shipped,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Development),
            new StatusChange("2", Local(2026, 11, 4, 9), Development, Done),
            new StatusChange("3", Local(2026, 11, 5, 9), Done, Review),
            new StatusChange("4", Local(2026, 11, 6, 9), Review, Done),
            new StatusChange("5", Local(2026, 11, 13, 9), Done, Shipped));

        await Assert.That(cycle!.Start).IsEqualTo(Local(2026, 11, 3, 9));
        await Assert.That(cycle.CalendarDays).IsEqualTo(3.0).Within(0.000001);
    }

    [Test]
    public async Task An_issue_that_never_entered_a_work_status_has_no_cycle()
    {
        var cycle = CycleOf(Done,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Blocked),
            new StatusChange("2", Local(2026, 11, 5, 9), Blocked, Done));

        await Assert.That(cycle).IsNull();
    }

    [Test]
    public async Task Weekends_and_holidays_inside_the_cycle_count_as_calendar_days_only()
    {
        // Friday noon to Wednesday noon, with Tuesday the 10th a holiday: five calendar days, of which
        // half of Friday, Monday and half of Wednesday are business days.
        var changes = new[]
        {
            new StatusChange("1", Local(2026, 11, 6, 12), New, Development),
            new StatusChange("2", Local(2026, 11, 11, 12), Development, Done),
        };
        var entries = StatusTimeline.Build(Created, Done, changes).Entries;

        var cycle = Cycles.Measure(entries, Roles, Local(2026, 11, 11, 12), TeamCalendar(new DateOnly(2026, 11, 10)));

        await Assert.That(cycle!.CalendarDays).IsEqualTo(5.0).Within(0.000001);
        await Assert.That(cycle.BusinessDays).IsEqualTo(2.0).Within(0.000001);
    }

    [Test]
    public async Task A_work_status_entered_at_the_delivery_moment_gives_a_cycle_of_zero_days()
    {
        // Moved to Development and on to Done in the same instant: the visit took no time, but it happened.
        var cycle = CycleOf(Done,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Development),
            new StatusChange("2", Local(2026, 11, 3, 9), Development, Done));

        await Assert.That(cycle).IsNotNull();
        await Assert.That(cycle!.Start).IsEqualTo(Local(2026, 11, 3, 9));
        await Assert.That(cycle.CalendarDays).IsEqualTo(0.0);
        await Assert.That(cycle.BusinessDays).IsEqualTo(0.0);
    }

    [Test]
    public async Task A_work_status_first_entered_after_the_delivery_moment_starts_no_cycle()
    {
        // Straight to Done, then reopened into Development and closed again in one instant. Delivery dates the
        // issue at its first move into Done (the visit to Development has no interval), so the only work
        // status it entered lies after its delivery moment.
        var cycle = CycleOf(Done,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Done),
            new StatusChange("2", Local(2026, 11, 5, 9), Done, Development),
            new StatusChange("3", Local(2026, 11, 5, 9), Development, Done));

        await Assert.That(cycle).IsNull();
    }

    [Test]
    public async Task A_reopen_that_is_closed_again_at_the_same_instant_is_still_a_reopen()
    {
        var reopens = ReopensOf(Done,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Development),
            new StatusChange("2", Local(2026, 11, 4, 9), Development, Done),
            new StatusChange("3", Local(2026, 11, 5, 9), Done, Development),
            new StatusChange("4", Local(2026, 11, 5, 9), Development, Done));

        await Assert.That(reopens).IsEqualTo(1);
    }

    [Test]
    public async Task An_issue_that_never_left_done_has_no_reopens()
    {
        var reopens = ReopensOf(Shipped,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Development),
            new StatusChange("2", Local(2026, 11, 4, 9), Development, Done),
            new StatusChange("3", Local(2026, 11, 5, 9), Done, Shipped));

        await Assert.That(reopens).IsEqualTo(0);
    }

    [Test]
    public async Task Every_move_out_of_a_done_status_into_one_that_is_not_done_is_a_reopen()
    {
        var reopens = ReopensOf(Blocked,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Development),
            new StatusChange("2", Local(2026, 11, 4, 9), Development, Done),
            new StatusChange("3", Local(2026, 11, 5, 9), Done, Development),
            new StatusChange("4", Local(2026, 11, 6, 9), Development, Done),
            new StatusChange("5", Local(2026, 11, 9, 9), Done, Shipped),
            new StatusChange("6", Local(2026, 11, 10, 9), Shipped, New),
            new StatusChange("7", Local(2026, 11, 11, 9), New, Done),
            new StatusChange("8", Local(2026, 11, 12, 9), Done, Blocked));

        await Assert.That(reopens).IsEqualTo(3);
    }

    /// <summary>The cycle of an issue now in <paramref name="currentStatusId"/>, delivered when <see cref="Delivery"/> says.</summary>
    private static Cycle? CycleOf(string currentStatusId, params StatusChange[] changes)
    {
        var timeline = StatusTimeline.Build(Created, currentStatusId, changes);
        var delivered = (Delivered)Delivery.Of(currentStatusId, timeline.Intervals, Roles).Value!;
        return Cycles.Measure(timeline.Entries, Roles, delivered.At, TeamCalendar());
    }

    private static int ReopensOf(string currentStatusId, params StatusChange[] changes) =>
        Cycles.ReopenCount(StatusTimeline.Build(Created, currentStatusId, changes).Entries, Roles);
}
