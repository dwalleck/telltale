using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.HarnessSteps;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// Which visits count toward a brief visit count (GLOSSARY: Brief visit; owner decisions D59 to D61; the
/// confirmed reading "Which visits count toward a sprint"; decision bv-D1 for a stay of no length), through
/// <c>derive</c> and then <c>query</c> or the report. Every test is about one issue of one person, with both
/// numbers of the minimum group size at zero, so that the count is shown whatever it is and reads 1 when the
/// issue is counted and 0 when it is not. Each expected row also gives the issue count of the status's time in
/// status row, to show the issue is in the row. Whether a count is withheld is not this class's subject.
/// Sprint 41 runs from Monday 5 January 2026, 09:00, to Monday the 19th, 10:00. The issue is created on Friday
/// 2 January in New and is in sprint 41 from then on, unless the test says otherwise.
/// </summary>
public class WhichVisitsCountTests
{
    /// <summary>Per sprint and listed status: the issues in the status's time in status row, and how many of them are counted.</summary>
    private const string CountsSql =
        """
        SELECT b.sprint_id, b.status_name, t.issue_count AS issues_in_the_row, b.brief_visit_issue_count
        FROM brief_visits_per_sprint b
        JOIN time_in_status_per_sprint t ON t.sprint_id = b.sprint_id AND t.status_id = b.status_id
        ORDER BY b.sprint_id, b.board_position;
        """;

    private const string CountsHead =
        "| sprint_id | status_name | issues_in_the_row | brief_visit_issue_count |\n| --- | --- | --- | --- |\n";

    private const string DevelopmentCounted = "| 41 | Fake Development | 1 | 1 |\n";

    private const string DevelopmentNotCounted = "| 41 | Fake Development | 1 | 0 |\n";

    [Test]
    public async Task A_status_entered_and_left_at_the_same_instant_is_a_brief_visit_when_the_issue_is_in_the_row_through_other_time()
    {
        // Two days in Development put the issue in the row. On Friday it is moved to Development and on to
        // Review at the same instant: a stay of no length, shorter than any threshold (story 20).
        await using var pipeline = SprintFortyOne();
        var friday = Local(2026, 1, 9, 10);
        Issue(pipeline)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Review)
            .Moved(friday, Workflow.Development)
            .Moved(friday, Workflow.Review)
            .Moved(Local(2026, 1, 13, 10), Workflow.Done);
        ListDevelopment(pipeline, minutes: 5);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        await Assert.That(await QueryAsync(pipeline, CountsSql)).IsEqualTo(CountsHead + DevelopmentCounted);
        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith("\n- Fake Development: 1 issue had a visit shorter than 5 minutes\n");
    }

    [Test]
    public async Task A_status_entered_and_left_at_the_same_instant_and_never_stayed_in_gives_no_line_as_it_has_no_row()
    {
        // The issue spent no time in Development, so Development has no time in status row in the sprint, and a
        // listed status with no row has no line (story 31).
        await using var pipeline = SprintFortyOne();
        var wednesday = Local(2026, 1, 7, 10);
        Issue(pipeline)
            .Moved(wednesday, Workflow.Development)
            .Moved(wednesday, Workflow.Review)
            .Moved(Local(2026, 1, 9, 10), Workflow.Done);
        ListDevelopment(pipeline, minutes: 5);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        await Assert.That(await QueryAsync(pipeline, CountsSql)).IsEqualTo(CountsHead);
        await Assert.That(await pipeline.ReadReportAsync(41)).DoesNotContain("a visit shorter than");
    }

    [Test]
    [Arguments(299, DevelopmentCounted)]
    [Arguments(300, DevelopmentNotCounted)]
    [Arguments(301, DevelopmentNotCounted)]
    public async Task A_stay_counts_just_under_the_threshold_and_not_at_it_or_over_it(int secondsInDevelopment, string expected)
    {
        await using var pipeline = SprintFortyOne();
        var wednesday = Local(2026, 1, 7, 10);
        Issue(pipeline)
            .Moved(wednesday, Workflow.Development)
            .Moved(wednesday.AddSeconds(secondsInDevelopment), Workflow.Review)
            .Moved(Local(2026, 1, 9, 10), Workflow.Done);
        ListDevelopment(pipeline, minutes: 5);

        await Assert.That(await CountsAfterDeriveAsync(pipeline)).IsEqualTo(CountsHead + expected);
    }

    [Test]
    [Arguments(29, "1 issue had a visit shorter than 0.5 minutes")]
    [Arguments(30, "0 issues had a visit shorter than 0.5 minutes")]
    public async Task A_threshold_of_half_a_minute_is_applied_as_written(int secondsInDevelopment, string expectedLine)
    {
        await using var pipeline = SprintFortyOne();
        var wednesday = Local(2026, 1, 7, 10);
        Issue(pipeline)
            .Moved(wednesday, Workflow.Development)
            .Moved(wednesday.AddSeconds(secondsInDevelopment), Workflow.Review)
            .Moved(Local(2026, 1, 9, 10), Workflow.Done);
        ListDevelopment(pipeline, minutes: 0.5);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith($"\n- Fake Development: {expectedLine}\n");
    }

    [Test]
    public async Task A_stay_the_issue_is_still_in_is_not_counted_however_short_it_is_so_far()
    {
        // Sprint 42 is still running when the clock reads Tuesday 3 February, 12:00. The issue was moved to
        // Development twenty seconds before that and is still there.
        await using var pipeline = Scenarios.SuppressionOff();
        pipeline.Jira.AddSprint42(stillRunning: true);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 19, 11)).AssignedTo(People.One).InSprints(42)
            .Moved(Local(2026, 2, 3, 11, 59).AddSeconds(40), Workflow.Development);
        ListDevelopment(pipeline, minutes: 5);

        await Assert.That(await CountsAfterDeriveAsync(pipeline)).IsEqualTo(CountsHead + "| 42 | Fake Development | 1 | 0 |\n");
    }

    [Test]
    public async Task An_issue_with_two_brief_visits_to_one_status_is_counted_once_for_it()
    {
        await using var pipeline = SprintFortyOne();
        var wednesday = Local(2026, 1, 7, 10);
        var thursday = Local(2026, 1, 8, 10);
        Issue(pipeline)
            .Moved(wednesday, Workflow.Development)
            .Moved(wednesday.AddSeconds(20), Workflow.Review)
            .Moved(thursday, Workflow.Development)
            .Moved(thursday.AddSeconds(20), Workflow.Review)
            .Moved(Local(2026, 1, 9, 10), Workflow.Done);
        ListDevelopment(pipeline, minutes: 5);

        await Assert.That(await CountsAfterDeriveAsync(pipeline)).IsEqualTo(CountsHead + DevelopmentCounted);
    }

    [Test]
    public async Task An_issue_with_brief_visits_to_two_listed_statuses_is_counted_once_under_each()
    {
        // Caught up in one go: Development and Review for twenty seconds each, then two days in QE.
        await using var pipeline = SprintFortyOne();
        var wednesday = Local(2026, 1, 7, 10);
        Issue(pipeline)
            .Moved(wednesday, Workflow.Development)
            .Moved(wednesday.AddSeconds(20), Workflow.Review)
            .Moved(wednesday.AddSeconds(40), Workflow.QE)
            .Moved(Local(2026, 1, 9, 10), Workflow.Done);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5, [Workflow.Review.Id] = 5 };

        await Assert.That(await CountsAfterDeriveAsync(pipeline)).IsEqualTo(
            CountsHead + DevelopmentCounted + "| 41 | Fake Review | 1 | 1 |\n");
    }

    [Test]
    public async Task Two_listed_statuses_with_different_thresholds_are_each_judged_by_their_own()
    {
        // Three minutes in Development and three in Review: under Development's five, over Review's two.
        await using var pipeline = SprintFortyOne();
        var wednesday = Local(2026, 1, 7, 10);
        Issue(pipeline)
            .Moved(wednesday, Workflow.Development)
            .Moved(wednesday.AddMinutes(3), Workflow.Review)
            .Moved(wednesday.AddMinutes(6), Workflow.QE)
            .Moved(Local(2026, 1, 9, 10), Workflow.Done);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5, [Workflow.Review.Id] = 2 };

        await Assert.That(await CountsAfterDeriveAsync(pipeline)).IsEqualTo(
            CountsHead + DevelopmentCounted + "| 41 | Fake Review | 1 | 0 |\n");
    }

    [Test]
    public async Task A_brief_visit_to_a_status_that_is_not_listed_gives_no_line_and_no_row()
    {
        // Twenty seconds in Development, which is not listed, and two days in Review, which is.
        await using var pipeline = SprintFortyOne();
        var wednesday = Local(2026, 1, 7, 10);
        Issue(pipeline)
            .Moved(wednesday, Workflow.Development)
            .Moved(wednesday.AddSeconds(20), Workflow.Review)
            .Moved(Local(2026, 1, 9, 10), Workflow.Done);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Review.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        await Assert.That(await QueryAsync(pipeline, CountsSql)).IsEqualTo(CountsHead + "| 41 | Fake Review | 1 | 0 |\n");
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).EndsWith(
            """
            - Issues delivered between sprints, in the whole store: 0
            - Fake Review: 0 issues had a visit shorter than 5 minutes

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task A_long_stay_cut_to_seconds_by_the_sprints_start_is_not_counted()
    {
        // In Development from the Friday before the sprint until twenty seconds after it starts on Monday at
        // 09:00: three days by the clock, and twenty seconds of them in the sprint.
        await using var pipeline = SprintFortyOne();
        Issue(pipeline)
            .Moved(Local(2026, 1, 2, 10), Workflow.Development)
            .Moved(Local(2026, 1, 5, 9).AddSeconds(20), Workflow.Review)
            .Moved(Local(2026, 1, 7, 10), Workflow.Done);
        ListDevelopment(pipeline, minutes: 5);

        await Assert.That(await CountsAfterDeriveAsync(pipeline)).IsEqualTo(CountsHead + DevelopmentNotCounted);
    }

    [Test]
    public async Task A_long_stay_cut_to_seconds_by_the_sprints_end_is_not_counted()
    {
        // In Development from twenty seconds before the sprint is completed on Monday the 19th at 10:00, for two days.
        await using var pipeline = SprintFortyOne();
        Issue(pipeline)
            .Moved(Local(2026, 1, 19, 9, 59).AddSeconds(40), Workflow.Development)
            .Moved(Local(2026, 1, 21, 10), Workflow.Done);
        ListDevelopment(pipeline, minutes: 5);

        await Assert.That(await CountsAfterDeriveAsync(pipeline)).IsEqualTo(CountsHead + DevelopmentNotCounted);
    }

    [Test]
    public async Task A_long_stay_cut_to_seconds_by_the_issue_joining_the_sprint_is_not_counted()
    {
        // Two days in Development, and the issue is put into the sprint twenty seconds before they end.
        await using var pipeline = SprintFortyOne();
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9)).AssignedTo(People.One)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .SprintsChanged(Local(2026, 1, 8, 8, 59).AddSeconds(40), 41)
            .Moved(Local(2026, 1, 8, 9), Workflow.Review)
            .Moved(Local(2026, 1, 9, 10), Workflow.Done);
        ListDevelopment(pipeline, minutes: 5);

        await Assert.That(await CountsAfterDeriveAsync(pipeline)).IsEqualTo(CountsHead + DevelopmentNotCounted);
    }

    [Test]
    public async Task A_long_stay_cut_to_seconds_by_the_issue_leaving_the_sprint_is_not_counted()
    {
        // Two days in Development, and the issue is taken out of the sprint twenty seconds after they begin.
        await using var pipeline = SprintFortyOne();
        Issue(pipeline)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .SprintsChanged(Local(2026, 1, 6, 9).AddSeconds(20))
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);
        ListDevelopment(pipeline, minutes: 5);

        await Assert.That(await CountsAfterDeriveAsync(pipeline)).IsEqualTo(CountsHead + DevelopmentNotCounted);
    }

    [Test]
    public async Task A_stay_across_a_weekend_is_longer_than_the_threshold_by_the_clock_and_is_not_counted()
    {
        // From Friday 23:59 to Monday 00:01: two minutes of business time, two days and two minutes by the clock.
        await using var pipeline = SprintFortyOne();
        Issue(pipeline)
            .Moved(Local(2026, 1, 9, 23, 59), Workflow.Development)
            .Moved(Local(2026, 1, 12, 0, 1), Workflow.Review)
            .Moved(Local(2026, 1, 13, 10), Workflow.Done);
        ListDevelopment(pipeline, minutes: 5);

        await Assert.That(await CountsAfterDeriveAsync(pipeline)).IsEqualTo(CountsHead + DevelopmentNotCounted);
    }

    [Test]
    public async Task A_stay_across_a_holiday_is_longer_than_the_threshold_by_the_clock_and_is_not_counted()
    {
        // Wednesday 7 January is a holiday here. From Tuesday 23:59 to Thursday 00:01: two minutes of business
        // time, a day and two minutes by the clock.
        await using var pipeline = SprintFortyOne();
        pipeline.Config["holidays"] = new JsonArray("2026-01-07");
        Issue(pipeline)
            .Moved(Local(2026, 1, 6, 23, 59), Workflow.Development)
            .Moved(Local(2026, 1, 8, 0, 1), Workflow.Review)
            .Moved(Local(2026, 1, 9, 10), Workflow.Done);
        ListDevelopment(pipeline, minutes: 5);

        await Assert.That(await CountsAfterDeriveAsync(pipeline)).IsEqualTo(CountsHead + DevelopmentNotCounted);
    }

    [Test]
    public async Task A_brief_visit_entirely_on_a_weekend_counts_when_the_issue_is_in_the_row_through_other_time()
    {
        // Which way, and why: the overlap rule asks whether the visit overlaps the time the row counts, which
        // the confirmed reading gives as "inside the sprint window, while a member". A Saturday inside the
        // sprint is inside that time, so the visit overlaps it and the issue is counted. Business time decides
        // only whether the issue is in the row at all: here two days in Development earlier in the week put it
        // there. (With no other time in Development it is not in the row and not counted:
        // BriefVisitCountTests.An_issue_that_is_not_in_the_status_row_is_not_counted_...)
        await using var pipeline = SprintFortyOne();
        var saturday = Local(2026, 1, 10, 10);
        Issue(pipeline)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Review)
            .Moved(saturday, Workflow.Development)
            .Moved(saturday.AddSeconds(20), Workflow.Review)
            .Moved(Local(2026, 1, 13, 10), Workflow.Done);
        ListDevelopment(pipeline, minutes: 5);

        await Assert.That(await CountsAfterDeriveAsync(pipeline)).IsEqualTo(CountsHead + DevelopmentCounted);
    }

    [Test]
    public async Task A_brief_visit_made_in_an_earlier_sprint_does_not_count_the_issue_in_a_later_sprint_where_its_stay_was_long()
    {
        // Twenty seconds in Development in sprint 41. The issue is carried over to sprint 42 (from Monday
        // 19 January, 10:30) and spends two days in Development there.
        await using var pipeline = Scenarios.SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var wednesday = Local(2026, 1, 7, 10);
        Issue(pipeline)
            .Moved(wednesday, Workflow.Development)
            .Moved(wednesday.AddSeconds(20), Workflow.Review)
            .SprintsChanged(Local(2026, 1, 19, 10, 15), 42)
            .Moved(Local(2026, 1, 20, 9), Workflow.Development)
            .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        ListDevelopment(pipeline, minutes: 5);

        await Assert.That(await CountsAfterDeriveAsync(pipeline)).IsEqualTo(
            CountsHead + DevelopmentCounted + "| 42 | Fake Development | 1 | 0 |\n");
    }

    [Test]
    public async Task Clocks_going_forward_inside_a_stay_do_not_lengthen_it()
    {
        // Sunday 8 March 2026: at 02:00 the team's clocks go to 03:00. In Development from 01:30 to 03:30 on
        // the wall: one hour between the two instants, under a threshold of 90 minutes, where the wall's two
        // hours would be over it. Two days in Development earlier in the week put the issue in the row.
        await using var pipeline = Scenarios.SuppressionOff(now: Local(2026, 4, 1, 12));
        pipeline.Jira.AddSprint(43, "Fake Sprint 43", Local(2026, 3, 2, 9), Local(2026, 3, 16, 9), completed: Local(2026, 3, 16, 10));
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 3, 2, 9)).AssignedTo(People.One).InSprints(43)
            .Moved(Local(2026, 3, 3, 9), Workflow.Development)
            .Moved(Local(2026, 3, 5, 9), Workflow.Review)
            .Moved(Local(2026, 3, 8, 1, 30), Workflow.Development)
            .Moved(Local(2026, 3, 8, 3, 30), Workflow.Review)
            .Moved(Local(2026, 3, 10, 10), Workflow.Done);
        ListDevelopment(pipeline, minutes: 90);

        await Assert.That(await CountsAfterDeriveAsync(pipeline)).IsEqualTo(CountsHead + "| 43 | Fake Development | 1 | 1 |\n");
    }

    /// <summary>Runs <c>backfill</c> and <c>derive</c>, and reads the counts with <c>query</c>.</summary>
    private static async Task<string> CountsAfterDeriveAsync(PipelineHarness pipeline)
    {
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        return await QueryAsync(pipeline, CountsSql);
    }

    /// <summary>A store of sprint 41 alone, with small-group suppression off.</summary>
    private static PipelineHarness SprintFortyOne()
    {
        var pipeline = Scenarios.SuppressionOff();
        pipeline.Jira.AddSprint41();
        return pipeline;
    }

    /// <summary>The one issue of a test: created on Friday 2 January 2026 in New, one person's, in sprint 41 from then on.</summary>
    private static FakeIssue Issue(PipelineHarness pipeline) =>
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9)).AssignedTo(People.One).InSprints(41);

    private static void ListDevelopment(PipelineHarness pipeline, double minutes) =>
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = minutes };
}
