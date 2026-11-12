using Telltale.Core.Intervals;
using Telltale.Core.Workflow;
using static Telltale.Core.Tests.TestTime;

namespace Telltale.Core.Tests;

public class DeliverySprintTests
{
    private static readonly DateTimeOffset AsOf = Local(2026, 12, 1, 12);

    private static readonly SprintWindow[] Sprints =
    [
        new("41", Local(2026, 11, 2, 9), PlannedEnd: Local(2026, 11, 16, 9), Completed: Local(2026, 11, 16, 10)),
        new("42", Local(2026, 11, 16, 11), PlannedEnd: Local(2026, 11, 30, 9), Completed: null),
    ];

    [Test]
    public async Task A_delivery_inside_a_sprint_window_belongs_to_that_sprint()
    {
        await Assert.That(DeliverySprint.For(Local(2026, 11, 10, 15), Sprints, AsOf)).IsEqualTo("41");
    }

    [Test]
    public async Task A_delivery_between_two_sprints_belongs_to_no_sprint()
    {
        await Assert.That(DeliverySprint.For(Local(2026, 11, 16, 10, 30), Sprints, AsOf)).IsNull();
    }

    [Test]
    public async Task A_delivery_at_the_moment_a_sprint_starts_belongs_to_it()
    {
        await Assert.That(DeliverySprint.For(Local(2026, 11, 2, 9), Sprints, AsOf)).IsEqualTo("41");
    }

    [Test]
    public async Task A_delivery_at_the_moment_a_sprint_is_completed_falls_outside_it()
    {
        await Assert.That(DeliverySprint.For(Local(2026, 11, 16, 10), Sprints, AsOf)).IsNull();
    }

    [Test]
    public async Task A_sprint_ends_at_its_completion_not_at_its_planned_end()
    {
        // Sprint 41 was planned to end at 9:00 and was completed at 10:00.
        await Assert.That(DeliverySprint.For(Local(2026, 11, 16, 9, 30), Sprints, AsOf)).IsEqualTo("41");
    }

    [Test]
    public async Task A_sprint_that_is_not_completed_runs_to_the_as_of_time()
    {
        // After sprint 42's planned end, but it has not been completed.
        await Assert.That(DeliverySprint.For(Local(2026, 11, 30, 15), Sprints, AsOf)).IsEqualTo("42");
    }
}
