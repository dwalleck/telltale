using Telltale.Pipeline.Tests.Harness;
using TUnit.Assertions.Enums;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The report's method note and data-quality footer: what the figures mean, the as-of time, how many
/// issues had changelog anomalies and how many were delivered between sprints.
/// </summary>
public class MethodNoteAndFooterTests
{
    private const string MethodNote =
        """

        ## Method

        - Durations are in business days: days that are neither a weekend nor a company holiday, each counted in full (24 hours) in the team's time zone (America/Chicago). Calendar days are shown alongside: elapsed time divided by 24 hours.
        - Cycle time runs from an issue's first entry into a work status to its last entry into a done status, for delivered issues only. It belongs to the sprint in which the issue was delivered.
        - Figures are medians and P85s. The median is the middle value, or the mean of the two middle values. The P85 is the smallest value with at least 85% of the values at or below it.
        - Time in status covers the statuses before done. An issue counts toward a status only if it spent more than zero business days there.
        - A figure is suppressed when the issues in the sprint have fewer than five distinct assignees, or when the figure itself has fewer than three distinct assignees behind it, so that it cannot show one person's work. The delivered count is subject to the first check only. A suppressed row still shows its issue count.
        - An interval that is still open (an issue still in a status, a sprint not yet completed) ends at the as-of time below.
        - Figures can include work since removed from Jira until the next backfill.

        """;

    [Test]
    public async Task The_report_ends_with_the_method_note_and_a_footer_giving_the_as_of_time()
    {
        // The clock reads 3 February 2026, noon, team time: that is the as-of time.
        await using var pipeline = new PipelineHarness(Local(2026, 2, 3, 12));
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).InSprints(41)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            MethodNote
            + """

              ---

              - As-of time: 2026-02-03 12:00 -06:00 (America/Chicago)
              - Issues with changelog anomalies, in the whole store: 0
              - Issues delivered between sprints, in the whole store: 0

              """);
    }

    [Test]
    public async Task An_issue_with_inconsistent_status_history_still_gets_a_timeline_is_logged_by_the_sync_and_is_counted_in_the_footer()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        // The second entry says the issue left Review, but the history before it left the issue in Development.
        var odd = pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .AddHistory(Local(2026, 1, 9, 9), "status", "status", Workflow.Review.Id, "Fake Review", Workflow.Done.Id, "Fake Done")
            .NowIn(Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 12, 9), Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo($"Changelog anomaly: issue {odd.Id}: its status history does not line up.\n");

        // The timeline follows each change's target status, so the issue is still delivered on the 9th, in sprint 41.
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: 2\n");
        await Assert.That(report).Contains("\n- Issues with changelog anomalies, in the whole store: 1\n");

        // Anomalies are stored in derived data: the issue's ID and a fixed message.
        var stored = await pipeline.QueryAsync<(string IssueId, string Message)>("SELECT issue_id, message FROM anomaly;");
        await Assert.That(stored).IsEquivalentTo(new[] { (odd.Id, "its status history does not line up") });
    }

    [Test]
    public async Task An_issue_whose_sprint_history_disagrees_with_its_sprint_field_is_logged_and_counted()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        // The history ends with the issue in sprints 41 and 42; the Sprint field lists 41 only.
        var odd = pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).InSprints(41)
            .SprintsChanged(Local(2026, 1, 19, 10), 41, 42)
            .InSprints(41);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo($"Changelog anomaly: issue {odd.Id}: its Sprint field history does not line up.\n");
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\n- Issues with changelog anomalies, in the whole store: 1\n");
    }

    [Test]
    public async Task An_issue_in_a_done_status_whose_history_ends_outside_done_is_an_anomaly_and_counts_once()
    {
        // Owner decision D8: not delivered, and a changelog anomaly. Its status history also does not line up
        // (it ends in Development), so the issue has two anomalies and is still one issue in the footer.
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        var odd = pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done)
            .Moved(Local(2026, 1, 12, 9), Workflow.Development)
            .NowIn(Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"Changelog anomaly: issue {odd.Id}: its status history does not line up.\n"
            + $"Changelog anomaly: issue {odd.Id}: it is in a done status but its status history ends outside done, so it is not counted as delivered.\n");

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: 0\n");
        await Assert.That(report).Contains("\n- Issues with changelog anomalies, in the whole store: 1\n");
        await Assert.That(report).Contains("\n- Issues delivered between sprints, in the whole store: 0\n");
    }

    [Test]
    public async Task An_issue_in_a_done_status_that_was_never_moved_into_done_is_an_anomaly()
    {
        // Owner decision D9: created in Done, so nothing dates a delivery. One was later moved on to Shipped.
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        var created = pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 8, 9), createdIn: Workflow.Done);
        var shipped = pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 8, 9), createdIn: Workflow.Done)
            .Moved(Local(2026, 1, 22, 9), Workflow.Shipped);

        // Not anomalies: an issue that is simply not done, and one moved into Done at the instant it was created.
        pipeline.Jira.AddIssue("FAKE-3", Local(2026, 1, 8, 9));
        pipeline.Jira.AddIssue("FAKE-4", Local(2026, 1, 8, 9))
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"Changelog anomaly: issue {created.Id}: it is in a done status but its history holds no move into done, so it is not counted as delivered.\n"
            + $"Changelog anomaly: issue {shipped.Id}: it is in a done status but its history holds no move into done, so it is not counted as delivered.\n");

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: 1\n");
        await Assert.That(report).Contains("\n- Issues with changelog anomalies, in the whole store: 2\n");
    }

    [Test]
    public async Task An_issue_delivered_between_two_sprints_is_in_neither_sprints_delivered_count_and_is_counted_in_the_footer()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        // One delivery inside each sprint, and one at 10:15 on the 19th: after sprint 41 was completed (10:00)
        // and before sprint 42 started (10:30).
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 14, 9), Workflow.Development)
            .Moved(Local(2026, 1, 19, 10, 15), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-3", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 20, 9), Workflow.Development)
            .Moved(Local(2026, 1, 22, 9), Workflow.Done);

        // Closed between the sprints with an abandoned resolution: not a delivery, so not in the footer's count either.
        pipeline.Config["abandonedResolutionIds"] = new System.Text.Json.Nodes.JsonArray("300002");
        pipeline.Jira.AddIssue("FAKE-4", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 19, 10, 20), Workflow.Done)
            .Resolved("300002");

        var exit41 = await pipeline.RunAsync("run", "--sprint", "41");
        var exit42 = await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit41).IsEqualTo(0);
        await Assert.That(exit42).IsEqualTo(0);
        foreach (var sprintId in new long[] { 41, 42 })
        {
            var report = await pipeline.ReadReportAsync(sprintId);
            await Assert.That(report).Contains("\nDelivered issues: 1\n");
            await Assert.That(report).Contains("\n- Issues delivered between sprints, in the whole store: 1\n");
        }

        var quality = await pipeline.QueryAsync<(long Anomalies, long BetweenSprints)>("SELECT anomaly_issue_count, delivered_between_sprints_count FROM data_quality;");
        await Assert.That(quality).IsEquivalentTo(new[] { (0L, 1L) });
    }

    [Test]
    public async Task The_report_and_the_anomaly_log_contain_no_issue_keys_and_no_names()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        // Every kind of line the note, the footer and the log can hold: an anomaly of each kind and a delivery between sprints.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).AssignedTo(People.One).InSprints(41)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development, by: People.Three)
            .AddHistory(Local(2026, 1, 9, 9), "status", "status", Workflow.Review.Id, "Fake Review", Workflow.Done.Id, "Fake Done", People.Three)
            .NowIn(Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 6, 9)).AssignedTo(People.Two).InSprints(41)
            .SprintsChanged(Local(2026, 1, 19, 10), 41, 42)
            .InSprints(41)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done)
            .Moved(Local(2026, 1, 12, 9), Workflow.Development)
            .NowIn(Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-3", Local(2026, 1, 8, 9), createdIn: Workflow.Shipped).AssignedTo(People.Four);
        pipeline.Jira.AddIssue("FAKE-4", Local(2026, 1, 6, 9)).AssignedTo(People.Five)
            .Moved(Local(2026, 1, 19, 10, 15), Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        var report = await pipeline.ReadReportAsync(41);
        var log = pipeline.Error.ToString();

        // The scan is only worth something if the lines it guards were written.
        await Assert.That(report).Contains("\n- Issues with changelog anomalies, in the whole store: 3\n");
        await Assert.That(report).Contains("\n- Issues delivered between sprints, in the whole store: 1\n");
        await Assert.That(log.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length).IsEqualTo(5);

        string[] forbidden = [FakeJira.ProjectKey + "-", .. pipeline.Jira.ForbiddenText, .. Workflow.BoardOrder.Select(status => status.Id), "Fake Review", "Fake Done"];
        var leaks = (from place in new[] { (Where: "the report", Text: report), (Where: "the log", Text: log), (Where: "standard output", Text: pipeline.Out.ToString().Replace(pipeline.Directory, "", StringComparison.Ordinal)) }
                     from text in forbidden
                     where place.Text.Contains(text, StringComparison.OrdinalIgnoreCase)
                     select $"'{text}' in {place.Where}").ToList();
        await Assert.That(leaks).IsEmpty();

        // Stored anomalies are an issue ID and one of the fixed messages, nothing else.
        var messages = await pipeline.QueryAsync<string>("SELECT DISTINCT message FROM anomaly ORDER BY message;");
        await Assert.That(messages).IsEquivalentTo(
            new[]
            {
                "it is in a done status but its history holds no move into done, so it is not counted as delivered",
                "it is in a done status but its status history ends outside done, so it is not counted as delivered",
                "its Sprint field history does not line up",
                "its status history does not line up",
            },
            CollectionOrdering.Matching);
    }

    /// <summary>Sprint 41 (5 to 19 January 2026) and sprint 42 (19 January to 2 February), both completed, with a half-hour gap between them.</summary>
    private static void AddTwoSprints(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        jira.AddSprint(42, "Fake Sprint 42", Local(2026, 1, 19, 10, 30), plannedEnd: Local(2026, 2, 2, 9), completed: Local(2026, 2, 2, 9, 30));
    }
}
