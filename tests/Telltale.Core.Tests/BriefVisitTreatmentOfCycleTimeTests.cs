using Telltale.Core.Intervals;
using Telltale.Core.Workflow;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Core.Tests;

/// <summary>
/// The treatment of brief visits for cycle time (GLOSSARY: Treatment, Cycle time; owner decisions D83 and D85):
/// which cycle a delivered issue's cycle time is taken from. Under <c>count</c> it is the issue's cycle, as it
/// is. Under <c>exclude</c> an issue whose cycle starts with a brief visit has none, and every other issue
/// keeps its own.
/// </summary>
public class BriefVisitTreatmentOfCycleTimeTests
{
    /// <summary>A cycle of two days that started on Tuesday 3 November 2026 at 10:00.</summary>
    private static readonly Cycle TwoDays = new(Local(2026, 11, 3, 10), CalendarDays: 2, BusinessDays: 2) { StartHistoryId = "1" };

    [Test]
    public async Task Under_count_a_cycle_that_starts_with_a_brief_visit_is_measured_as_it_is()
    {
        var measured = BriefVisitTreatments.CycleToMeasure(TwoDays, startsWithBriefVisit: true, new CountBriefVisits());

        await Assert.That(measured).IsSameReferenceAs(TwoDays);
    }

    [Test]
    public async Task Under_exclude_a_cycle_that_starts_with_a_brief_visit_is_left_out()
    {
        var measured = BriefVisitTreatments.CycleToMeasure(TwoDays, startsWithBriefVisit: true, new ExcludeBriefVisits());

        await Assert.That(measured).IsNull();
    }

    [Test]
    public async Task Under_exclude_a_cycle_that_does_not_start_with_a_brief_visit_is_measured_as_it_is()
    {
        var measured = BriefVisitTreatments.CycleToMeasure(TwoDays, startsWithBriefVisit: false, new ExcludeBriefVisits());

        await Assert.That(measured).IsSameReferenceAs(TwoDays);
    }
}
