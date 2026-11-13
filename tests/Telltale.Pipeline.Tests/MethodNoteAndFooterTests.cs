using Telltale.Pipeline.Tests.Harness;
using Telltale.Reporting;
using Telltale.Storage.Sqlite;
using TUnit.Assertions.Enums;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The report's method note and data-quality footer: what the figures mean, the as-of time, how many
/// issues had changelog anomalies and how many were delivered between sprints.
/// </summary>
public class MethodNoteAndFooterTests
{
    [Test]
    public async Task The_report_ends_with_the_method_note_and_a_footer_giving_the_as_of_time()
    {
        // The clock reads 3 February 2026, noon, team time: that is the as-of time.
        await using var pipeline = new PipelineHarness(Local(2026, 2, 3, 12));
        AddTwoSprintsWithATeam(pipeline.Jira);
        AddThreeDeliveriesBetweenSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).InSprints(41)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        // Assert the computed footer, not a verbatim copy of the explanatory prose above it.
        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            """

            ---

            - As-of time: 2026-02-03 12:00 -06:00 (America/Chicago)
            - Issues with changelog anomalies, in the whole store: suppressed
            - Issues delivered between sprints, in the whole store: 3

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task An_issue_with_inconsistent_status_history_still_gets_a_timeline_is_logged_and_is_counted_in_the_footer()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsWithATeam(pipeline.Jira);
        var othersLogged = AddThreeAnomalies(pipeline.Jira);

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
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Changelog anomalies: 4 across 4 issues, 4 new since the previous derive.\n"
            + othersLogged
            + $"Changelog anomaly: issue {odd.Id}: its status history does not line up.\n");

        // The timeline follows each change's target status, so the issue is still delivered on the 9th, in sprint 41.
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: 5\n");
        await Assert.That(report).Contains("\n- Issues with changelog anomalies, in the whole store: 4\n");

        // Anomalies are stored in derived data: the issue's ID and a fixed message.
        var stored = await pipeline.QueryAsync<(string IssueId, string Message)>("SELECT issue_id, message FROM anomaly WHERE issue_id = @id;", new { id = odd.Id });
        await Assert.That(stored).IsEquivalentTo(new[] { (odd.Id, "its status history does not line up") });
    }

    [Test]
    public async Task Derive_logs_the_total_and_then_only_the_anomalies_the_previous_derive_did_not_have()
    {
        // Timelines are built by derive, so derive finds and logs the anomalies; collecting and reporting log nothing.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsWithATeam(pipeline.Jira);
        var first = pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 8, 9), createdIn: Workflow.Done);

        await pipeline.RunAllAsync(["backfill"]);
        var afterBackfill = pipeline.Error.ToString();
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);
        var afterFirstDerive = pipeline.Error.ToString().ReplaceLineEndings("\n");

        // A second issue with an anomaly arrives; the first one's is still there and is not logged again.
        var second = pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 8, 9), createdIn: Workflow.Shipped);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var afterSecondDerive = pipeline.Error.ToString().ReplaceLineEndings("\n");

        // Nothing changed: the total is still said, and no anomaly is repeated.
        await pipeline.RunAllAsync(["derive"]);

        const string Message = "it is in a done status but its history holds no move into done, so it is not counted as delivered";
        var firstLines = "Changelog anomalies: 1 across 1 issue, 1 new since the previous derive.\n"
            + $"Changelog anomaly: issue {first.Id}: {Message}.\n";
        var secondLines = "Changelog anomalies: 2 across 2 issues, 1 new since the previous derive.\n"
            + $"Changelog anomaly: issue {second.Id}: {Message}.\n";
        await Assert.That(afterBackfill).IsEmpty();
        await Assert.That(afterFirstDerive).IsEqualTo(firstLines);
        await Assert.That(afterSecondDerive).IsEqualTo(firstLines + secondLines);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            firstLines + secondLines + "Changelog anomalies: 2 across 2 issues, 0 new since the previous derive.\n");

        // The store holds the full list, whatever was logged.
        var stored = await pipeline.QueryAsync<string>("SELECT issue_id FROM anomaly ORDER BY issue_id;");
        await Assert.That(stored).IsEquivalentTo(new[] { first.Id, second.Id }, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("statusRoles")]
    [Arguments("abandonedResolutionIds")]
    public async Task A_report_is_refused_when_the_status_roles_or_the_abandoned_resolutions_changed_since_derive(string setting)
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsWithATeam(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        // What is delivered, and when a cycle starts, was decided with the old setting.
        if (setting == "statusRoles")
        {
            pipeline.Config["statusRoles"]![Workflow.Review.Id] = "blocked";
        }
        else
        {
            pipeline.Config["abandonedResolutionIds"] = new System.Text.Json.Nodes.JsonArray("700001");
        }

        var exit = await pipeline.RunAsync("report", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "statusRoles or abandonedResolutionIds in the configuration file changed since the derived data was built. Run derive first.\n");
        await Assert.That(Directory.Exists(pipeline.ReportDirectory)).IsFalse();

        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\n## Method\n");
    }

    [Test]
    public async Task A_report_is_not_refused_when_lists_of_the_configuration_are_only_written_in_another_order()
    {
        // What derive records is compared setting by setting, and the order a list is written in changes
        // nothing a derived value depends on. (The order of statusRoles is board order, and is not such a list.)
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprints41And42();
        pipeline.Jira.AddTeam(41, 42);
        pipeline.Config["abandonedResolutionIds"] = new System.Text.Json.Nodes.JsonArray("300001", "300002");
        pipeline.Config["holidays"] = new System.Text.Json.Nodes.JsonArray("2026-01-19", "2026-01-01");
        pipeline.Config["baselineSprintIds"] = new System.Text.Json.Nodes.JsonArray("41", "42");
        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        pipeline.Config["abandonedResolutionIds"] = new System.Text.Json.Nodes.JsonArray(" 300002", "300001");
        pipeline.Config["holidays"] = new System.Text.Json.Nodes.JsonArray("2026-01-01", "2026-01-19");
        pipeline.Config["baselineSprintIds"] = new System.Text.Json.Nodes.JsonArray("42", "41", "41");
        var exit = await pipeline.RunAsync("report", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 3\n");
    }

    [Test]
    [Arguments("timeZone")]
    [Arguments("holidays")]
    public async Task A_report_is_refused_when_the_time_zone_or_the_holidays_changed_since_derive(string setting)
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsWithATeam(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        // The business days in the store were counted with the old setting.
        pipeline.Config[setting] = setting == "timeZone" ? "Europe/Berlin" : new System.Text.Json.Nodes.JsonArray("2026-01-08");
        var exit = await pipeline.RunAsync("report", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "The time zone or the holidays in the configuration changed since the derived data was built. Run derive first.\n");
        await Assert.That(Directory.Exists(pipeline.ReportDirectory)).IsFalse();

        // Derive again and the report is written, with the new setting.
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\n## Method\n");
    }

    [Test]
    public async Task The_method_note_and_the_footer_name_the_time_zone_the_figures_were_derived_with()
    {
        await using var pipeline = new PipelineHarness(Local(2026, 2, 3, 12));
        AddTwoSprintsWithATeam(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        // Rendered with another zone than derive used: the note and the as-of time still speak of derive's.
        var report = await SprintReport.RenderAsync(new SqliteStore(pipeline.DatabasePath), "41", TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"), baselineSprintIds: []);

        await Assert.That(report).Contains("in the team's time zone (America/Chicago). Calendar days");
        await Assert.That(report).Contains("\n- As-of time: 2026-02-03 12:00 -06:00 (America/Chicago)\n");
        await Assert.That(report).DoesNotContain("Berlin");
    }

    [Test]
    public async Task A_report_from_a_store_without_the_data_quality_view_says_to_derive_again()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsWithATeam(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        await pipeline.ExecuteAsync("DROP VIEW data_quality;");

        var exit = await pipeline.RunAsync("report", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "The metric view data_quality is not in the store. Run derive first.\n");
        await Assert.That(Directory.Exists(pipeline.ReportDirectory)).IsFalse();
    }

    [Test]
    public async Task An_issue_id_that_is_not_digits_stops_the_run_and_is_not_quoted()
    {
        // The issue ID is the one value of Jira's that reaches the anomaly log, so it is checked where it enters.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsWithATeam(pipeline.Jira);
        pipeline.Jira.Handler.Override(HttpMethod.Post, "/rest/api/3/search/jql", _ => true,
            StubHttpHandler.Json("""{"isLast":true,"issues":[{"id":"900001 Fake Person One","key":"FAKE-1","fields":{"issuetype":{"id":"800001","subtask":false,"hierarchyLevel":0},"status":{"id":"600001"},"created":"2026-01-06T15:00:00.000+0000","updated":"2026-01-06T15:00:00.000+0000"}}]}"""));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Jira's issue search answered in a shape this tool does not understand (FormatException). Nothing was stored.\n");
    }

    [Test]
    public async Task An_issue_whose_sprint_history_disagrees_with_its_sprint_field_is_logged_and_counted()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsWithATeam(pipeline.Jira);
        var othersLogged = AddThreeAnomalies(pipeline.Jira);

        // The history ends with the issue in sprints 41 and 42; the Sprint field lists 41 only.
        var odd = pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).InSprints(41)
            .SprintsChanged(Local(2026, 1, 19, 10), 41, 42)
            .InSprints(41);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Changelog anomalies: 4 across 4 issues, 4 new since the previous derive.\n"
            + othersLogged
            + $"Changelog anomaly: issue {odd.Id}: its Sprint field history does not line up.\n");
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\n- Issues with changelog anomalies, in the whole store: 4\n");
    }

    [Test]
    public async Task An_issue_in_a_done_status_whose_history_ends_outside_done_is_an_anomaly_and_counts_once()
    {
        // Owner decision D8: not delivered, and a changelog anomaly. Its status history also does not line up
        // (it ends in Development), so the issue has two anomalies and is still one issue in the footer.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsWithATeam(pipeline.Jira);
        var othersLogged = AddThreeAnomalies(pipeline.Jira);
        AddThreeDeliveriesBetweenSprints(pipeline.Jira);
        var odd = pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done)
            .Moved(Local(2026, 1, 12, 9), Workflow.Development)
            .NowIn(Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Changelog anomalies: 5 across 4 issues, 5 new since the previous derive.\n"
            + othersLogged
            + $"Changelog anomaly: issue {odd.Id}: its status history does not line up.\n"
            + $"Changelog anomaly: issue {odd.Id}: it is in a done status but its status history ends outside done, so it is not counted as delivered.\n");

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: 3\n");
        await Assert.That(report).Contains("\n- Issues with changelog anomalies, in the whole store: 4\n");
        await Assert.That(report).Contains("\n- Issues delivered between sprints, in the whole store: 3\n");
    }

    [Test]
    public async Task An_issue_in_a_done_status_that_was_never_moved_into_done_is_an_anomaly()
    {
        // Owner decision D9: created in Done, so nothing dates a delivery. One was later moved on to Shipped.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsWithATeam(pipeline.Jira);
        var othersLogged = AddThreeAnomalies(pipeline.Jira);
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
            "Changelog anomalies: 5 across 5 issues, 5 new since the previous derive.\n"
            + othersLogged
            + $"Changelog anomaly: issue {created.Id}: it is in a done status but its history holds no move into done, so it is not counted as delivered.\n"
            + $"Changelog anomaly: issue {shipped.Id}: it is in a done status but its history holds no move into done, so it is not counted as delivered.\n");

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: 4\n");
        await Assert.That(report).Contains("\n- Issues with changelog anomalies, in the whole store: 5\n");
    }

    [Test]
    public async Task An_issue_delivered_between_two_sprints_is_in_neither_sprints_delivered_count_and_is_counted_in_the_footer()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsWithATeam(pipeline.Jira);

        AddThreeDeliveriesBetweenSprints(pipeline.Jira);

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
            await Assert.That(report).Contains("\nDelivered issues: 4\n");
            await Assert.That(report).Contains("\n- Issues delivered between sprints, in the whole store: 4\n");
        }

        var quality = await pipeline.QueryAsync<(long? Anomalies, long? BetweenSprints, long Suppressed)>("SELECT anomaly_issue_count, delivered_between_sprints_count, suppressed FROM data_quality;");
        await Assert.That(quality).IsEquivalentTo(new (long?, long?, long)[] { (null, 4, 0) });
    }

    [Test]
    public async Task The_report_and_the_anomaly_log_contain_no_issue_keys_and_no_names()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsWithATeam(pipeline.Jira);

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
        foreach (var (person, number) in new[] { (People.Five, 4), (People.One, 5), (People.Two, 6) })
        {
            pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 6, 9)).AssignedTo(person)
                .Moved(Local(2026, 1, 19, 10, 15), Workflow.Done);
        }

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        var report = await pipeline.ReadReportAsync(41);
        var log = pipeline.Error.ToString();

        // The scan is only worth something if the lines it guards were written.
        await Assert.That(report).Contains("\n- Issues with changelog anomalies, in the whole store: 3\n");
        await Assert.That(report).Contains("\n- Issues delivered between sprints, in the whole store: 3\n");
        await Assert.That(log.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length).IsEqualTo(6);

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

    [Test]
    public async Task The_footers_counts_read_suppressed_when_the_whole_store_has_fewer_than_five_assignees()
    {
        // Review finding 2: with four people in the store, "1 issue delivered between sprints" is close to naming one of them.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprints41And42();
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 8, 9), createdIn: Workflow.Done).AssignedTo(People.One);
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 6, 9)).AssignedTo(People.Two)
            .Moved(Local(2026, 1, 19, 10, 15), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-3", Local(2026, 1, 6, 9)).AssignedTo(People.Three);
        pipeline.Jira.AddIssue("FAKE-4", Local(2026, 1, 6, 9)).AssignedTo(People.Four);
        pipeline.Jira.AddIssue("FAKE-5", Local(2026, 1, 6, 9));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            """

            ---

            - As-of time: 2026-02-03 12:00 -06:00 (America/Chicago)
            - Issues with changelog anomalies, in the whole store: suppressed
            - Issues delivered between sprints, in the whole store: suppressed

            """.ReplaceLineEndings("\n"));

        var quality = await pipeline.QueryAsync<(long? Anomalies, long? BetweenSprints, long Suppressed)>(
            "SELECT anomaly_issue_count, delivered_between_sprints_count, suppressed FROM data_quality;");
        await Assert.That(quality).IsEquivalentTo(new (long?, long?, long)[] { (null, null, 1) });
    }

    [Test]
    [Arguments(2, "suppressed", "suppressed")]
    [Arguments(3, "3", "3")]
    public async Task A_footer_count_is_shown_only_with_three_distinct_assignees_among_the_issues_it_counts(int people, string anomalies, string betweenSprints)
    {
        // ADR-0003's rule for a result grouped by issue attributes, as in the tables: a team of five (here the
        // whole store's) and three distinct assignees behind the figure. Each person has one issue with an
        // anomaly (created in Done) and one delivered between the sprints; the first person has a second of each.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsWithATeam(pipeline.Jira);
        foreach (var (person, number) in People.Team.Take(people).Append(People.One).Take(3).Select((person, index) => (person, index + 1)))
        {
            pipeline.Jira.AddIssue($"FAKE-1{number}", Local(2026, 1, 8, 9), createdIn: Workflow.Done).AssignedTo(person);
            pipeline.Jira.AddIssue($"FAKE-2{number}", Local(2026, 1, 6, 9)).AssignedTo(person)
                .Moved(Local(2026, 1, 19, 10, 15), Workflow.Done);
        }

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            $"""

            ---

            - As-of time: 2026-02-03 12:00 -06:00 (America/Chicago)
            - Issues with changelog anomalies, in the whole store: {anomalies}
            - Issues delivered between sprints, in the whole store: {betweenSprints}

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task A_footer_count_of_nothing_has_nobody_behind_it_and_reads_suppressed()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsWithATeam(pipeline.Jira);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        var quality = await pipeline.QueryAsync<(long? Anomalies, long? BetweenSprints, long Suppressed)>(
            "SELECT anomaly_issue_count, delivered_between_sprints_count, suppressed FROM data_quality;");
        await Assert.That(quality).IsEquivalentTo(new (long?, long?, long)[] { (null, null, 0) });
    }

    /// <summary>
    /// Sprint 41 (5 to 19 January 2026) and sprint 42 (19 January to 2 February), both completed, with a half-hour
    /// gap between them, and a team of five in both: figures are shown only for a team (ADR-0003). The team's
    /// five issues sit in New and have no anomaly.
    /// </summary>
    private static void AddTwoSprintsWithATeam(FakeJira jira)
    {
        jira.AddSprints41And42();
        jira.AddTeam(41, 42);
    }

    private const string NoMoveIntoDone = "it is in a done status but its history holds no move into done, so it is not counted as delivered";

    /// <summary>
    /// Three issues with an anomaly (created in Done on 8 January), one each for three people, so that the
    /// footer shows its count of anomalies: a count needs three distinct assignees among its issues. A test's
    /// own anomalies add to these three. Returns the lines derive logs for them, in the order it logs them
    /// when they are added before the test's other anomalous issues.
    /// </summary>
    private static string AddThreeAnomalies(FakeJira jira) =>
        string.Concat(People.Team.Take(3).Select((person, index) =>
            $"Changelog anomaly: issue {jira.AddIssue($"FAKE-80{index + 1}", Local(2026, 1, 8, 9), createdIn: Workflow.Done).AssignedTo(person).Id}: {NoMoveIntoDone}.\n"));

    /// <summary>
    /// Three issues delivered between sprints 41 and 42 (at 10:10 on 19 January), one each for three people, so
    /// that the footer shows its count of them. A test's own deliveries between sprints add to these three.
    /// </summary>
    private static void AddThreeDeliveriesBetweenSprints(FakeJira jira)
    {
        foreach (var (person, number) in People.Team.Take(3).Select((person, index) => (person, index + 1)))
        {
            jira.AddIssue($"FAKE-81{number}", Local(2026, 1, 6, 9)).AssignedTo(person).Moved(Local(2026, 1, 19, 10, 10), Workflow.Done);
        }
    }

}
