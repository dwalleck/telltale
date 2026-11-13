using System.Text.Json.Nodes;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests.Harness;

/// <summary>
/// Fixtures more than one test class is set in, defined once: the <c>minimumGroupSize</c> setting as the
/// configuration file writes it, sprints of two working weeks, and issues that run a fixed course through a
/// sprint. A test class brings them in with <c>using static</c> and adds what only it needs.
/// Sprint 41 starts on Monday 5 January 2026, sprint 42 on the 19th and sprint 43 on 2 February.
/// </summary>
public static class Scenarios
{
    /// <summary>The <c>minimumGroupSize</c> setting with the two numbers, for <see cref="PipelineHarness.Config"/>.</summary>
    public static JsonObject MinimumGroupSize(int team, int assigneesBehindAFigure) =>
        new() { ["team"] = team, ["assigneesBehindAFigure"] = assigneesBehindAFigure };

    /// <summary>A harness whose configuration turns small-group suppression off: both numbers zero.</summary>
    public static PipelineHarness SuppressionOff(DateTimeOffset? now = null)
    {
        var pipeline = new PipelineHarness(now);
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team: 0, assigneesBehindAFigure: 0);
        return pipeline;
    }

    /// <summary>
    /// Sprint 41 with four people, one delivered 3-point issue each: created as the sprint starts (Monday
    /// 5 January 09:00), in development from Tuesday 09:00, done Thursday 09:00. So one business day in New and
    /// a cycle of two days, each. Four assignees are fewer than the default team of five: a sprint the default
    /// numbers withhold whole.
    /// </summary>
    public static void AddSprint41WithFourAssignees(FakeJira jira)
    {
        jira.AddSprint41();
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            jira.AddIssue($"FAKE-{number}", Local(2026, 1, 5, 9)).AssignedTo(person).Estimated(3).InSprints(41)
                .Moved(Local(2026, 1, 6, 9), Workflow.Development)
                .Moved(Local(2026, 1, 8, 9), Workflow.Done);
        }
    }

    /// <summary>Sprint 41 alone, as two working weeks: Monday 09:00 to the second Friday 17:00, completed as planned.</summary>
    public static void AddSprintFortyOneOfTwoWorkingWeeks(FakeJira jira) =>
        jira.AddSprint41(plannedEnd: Local(2026, 1, 16, 17), completed: Local(2026, 1, 16, 17));

    /// <summary>The harness's two sprints as two working weeks each: Monday 09:00 to the second Friday 17:00, completed as planned.</summary>
    public static void AddTwoSprintsOfTwoWorkingWeeks(FakeJira jira)
    {
        AddSprintFortyOneOfTwoWorkingWeeks(jira);
        jira.AddSprint42(start: Local(2026, 1, 19, 9), plannedEnd: Local(2026, 1, 30, 17), completed: Local(2026, 1, 30, 17));
    }

    /// <summary>
    /// Sprint 42 with a team of six: three people's 3-point issues, a day in Development and 1, 2 and 3 in Review
    /// (cycle times 2, 3 and 4; the last ends after a weekend, 6 calendar days), and three more people's
    /// 5-point issues, two days each.
    /// </summary>
    public static void AddSprintFortyTwoWithThreeAssigneesInEveryRow(FakeJira jira)
    {
        Delivered(jira, 42, "FAKE-11", points: 3, People.One, days: 1, reviewDays: 1);
        Delivered(jira, 42, "FAKE-12", points: 3, People.Two, days: 1, reviewDays: 2);
        Delivered(jira, 42, "FAKE-13", points: 3, People.Three, days: 1, reviewDays: 3);
        foreach (var (person, number) in new[] { (People.Four, 21), (People.Five, 22), (People.Six, 23) })
        {
            Delivered(jira, 42, $"FAKE-{number}", points: 5, person, days: 2);
        }
    }

    /// <summary>
    /// An issue of sprint 41, 42 or 43, created as the sprint starts (Monday 09:00) and in Development from
    /// Tuesday 09:00 for <paramref name="days"/> business days; then, with <paramref name="reviewDays"/>, in
    /// Review for that many; then done. So it spends a day in New, and its cycle time is the two together (at
    /// most eight business days, so it is done inside the sprint). Null <paramref name="points"/> leaves it
    /// unestimated and a null <paramref name="assignee"/> unassigned.
    /// </summary>
    public static FakeIssue Delivered(FakeJira jira, long sprintId, string key, double? points, FakePerson? assignee, int days, int reviewDays = 0)
    {
        var tuesday = MondayOf(sprintId).AddDays(1);

        // Tuesday plus four business days is the next Monday: a weekend lies between.
        DateTimeOffset After(int businessDays) => tuesday.AddDays(businessDays < 4 ? businessDays : businessDays + 2);

        var issue = StillInDevelopment(jira, sprintId, key, assignee);
        if (points is { } estimate)
        {
            issue.Estimated(estimate);
        }

        if (reviewDays > 0)
        {
            issue.Moved(After(days), Workflow.Review);
        }

        return issue.Moved(After(days + reviewDays), Workflow.Done);
    }

    /// <summary>
    /// An issue of the sprint, created as it starts (Monday 09:00) and moved to Development on Tuesday 09:00,
    /// where it still is. A null <paramref name="assignee"/> leaves it unassigned.
    /// </summary>
    public static FakeIssue StillInDevelopment(FakeJira jira, long sprintId, string key, FakePerson? assignee)
    {
        var monday = MondayOf(sprintId);
        var issue = jira.AddIssue(key, monday).InSprints(sprintId).Moved(monday.AddDays(1), Workflow.Development);
        return assignee is null ? issue : issue.AssignedTo(assignee);
    }

    /// <summary>The Monday, 09:00, on which sprint 41, 42 or 43 starts in these fixtures.</summary>
    public static DateTimeOffset MondayOf(long sprintId) => sprintId switch
    {
        41 => Local(2026, 1, 5, 9),
        42 => Local(2026, 1, 19, 9),
        43 => Local(2026, 2, 2, 9),
        _ => throw new ArgumentOutOfRangeException(nameof(sprintId), sprintId, "The fixtures have sprints 41, 42 and 43."),
    };
}
