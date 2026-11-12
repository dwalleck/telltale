using System.Text.Json.Nodes;
using Telltale.Core;
using Telltale.Pipeline.Tests.Harness;
using Telltale.Reporting;
using Telltale.Storage.Sqlite;
using TUnit.Assertions.Enums;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The report's time in status table: per status, how many issues spent time in it during the sprint
/// and the median business days they spent, counting only time inside the sprint window and while
/// the issue was a member of the sprint (GLOSSARY: Time in status).
/// A figure is shown only for a team of five with three assignees behind it (SuppressionTests), so most tests
/// here have the fake serve each issue once per team member: a count is five times the issues the test adds,
/// and a median is what it is for those issues alone.
/// </summary>
public class TimeInStatusTableTests
{
    [Test]
    public async Task The_report_shows_the_issue_count_and_median_business_days_for_each_status_issues_were_in_during_the_sprint()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.ServeEachIssueOncePer(People.Team);

        // In the sprint from before it started. New until Tuesday 09:00, Development until Thursday 09:00.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9)).InSprints(41)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);

        // New counts from the sprint's start on Monday 09:00, not from the issue's creation the Friday before.
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            """

            ## Time in status

            | Status | Issues | Median (business days) |
            | --- | ---: | ---: |
            | Fake New | 5 | 1.0 |
            | Fake Development | 5 | 2.0 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task An_issue_added_to_the_sprint_after_it_started_only_contributes_time_from_when_it_joined()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.ServeEachIssueOncePer(People.Team);

        // In Development since the Friday before the sprint; added to the sprint on Wednesday 09:00; Done on Friday 09:00.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9), createdIn: Workflow.Development)
            .SprintsChanged(Local(2026, 1, 7, 9), 41)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        // Wednesday 09:00 to Friday 09:00. From the sprint's start it would be 4.0.
        await Assert.That(TimeInStatusRows(await pipeline.ReadReportAsync(41))).IsEquivalentTo(
            new[] { "| Fake Development | 5 | 2.0 |" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task An_issue_removed_mid_sprint_only_contributes_time_while_it_was_a_member()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.ServeEachIssueOncePer(People.Team);

        // In the sprint and in Development from creation; taken out of the sprint on Thursday 09:00; Done the next Wednesday.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9), createdIn: Workflow.Development).InSprints(41)
            .SprintsChanged(Local(2026, 1, 8, 9))
            .Moved(Local(2026, 1, 14, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        // Monday 09:00 (the sprint's start) to Thursday 09:00. Up to the move to Done it would be 7.0.
        await Assert.That(TimeInStatusRows(await pipeline.ReadReportAsync(41))).IsEquivalentTo(
            new[] { "| Fake Development | 5 | 3.0 |" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task An_issue_carried_over_contributes_to_each_sprint_only_within_that_sprints_window()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.ServeEachIssueOncePer(People.Team);

        // Development from Thursday 15 January 09:00. Sprint 41 completes on Monday 19 January 09:00 and Jira carries
        // the issue over: the Sprint field then lists both sprints. Sprint 42 starts at 12:00 the same day.
        // Review from Wednesday 21 January 12:00, Done at the start of Friday 23 January.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9)).InSprints(41)
            .Moved(Local(2026, 1, 15, 9), Workflow.Development)
            .SprintsChanged(Local(2026, 1, 19, 9), 41, 42)
            .Moved(Local(2026, 1, 21, 12), Workflow.Review)
            .Moved(Local(2026, 1, 23), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");
        await pipeline.RunAsync("run", "--sprint", "42");

        // Sprint 41: New from Monday 5 January 09:00 to Thursday 15 January 09:00 (8 business days), then Development
        // to the sprint's completion (Thursday 09:00 to Monday 09:00, over a weekend).
        await Assert.That(TimeInStatusRows(await pipeline.ReadReportAsync(41))).IsEquivalentTo(
            new[] { "| Fake New | 5 | 8.0 |", "| Fake Development | 5 | 2.0 |" }, CollectionOrdering.Matching);

        // Sprint 42: Development from the sprint's start (Monday 12:00) to Wednesday 12:00, then Review to Friday 00:00.
        await Assert.That(TimeInStatusRows(await pipeline.ReadReportAsync(42))).IsEquivalentTo(
            new[] { "| Fake Development | 5 | 2.0 |", "| Fake Review | 5 | 1.5 |" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Statuses_appear_in_board_order_with_the_median_over_the_issues_that_were_in_each_and_a_status_nobody_was_in_is_omitted()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        // Board order is the order the configuration lists the statuses in. Here Blocked sits before Development,
        // which is neither the order of the status IDs nor the order Jira lists the statuses in.
        FakeStatus[] boardOrder = [Workflow.New, Workflow.Approved, Workflow.Committed, Workflow.Blocked, Workflow.Development, Workflow.Review, Workflow.QE, Workflow.Done, Workflow.Shipped];
        pipeline.Config["statusRoles"] = new JsonObject(boardOrder.Select(status => KeyValuePair.Create(status.Id, (JsonNode?)status.Role)));

        // Five issues, one per person, so each row's median is over exactly the values listed here.
        // Committed 1 day; Development 1 day, Blocked 1 day, Development 1 more day.
        InCommitted(People.One)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 7, 9), Workflow.Blocked)
            .Moved(Local(2026, 1, 8, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);

        // Committed 2 days; Development 1 day.
        InCommitted(People.Two)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);

        // Left Committed the moment the sprint started, so no time in it; Development 4 days.
        InCommitted(People.Three)
            .Moved(Local(2026, 1, 5, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);

        // Committed 3 days; Blocked 1 day; Development Friday 09:00 to Monday 09:00, 1 business day.
        InCommitted(People.Four)
            .Moved(Local(2026, 1, 8, 9), Workflow.Blocked)
            .Moved(Local(2026, 1, 9, 9), Workflow.Development)
            .Moved(Local(2026, 1, 12, 9), Workflow.Done);

        // Committed 4 days; Blocked Friday 09:00 to Tuesday 09:00, 2 business days; Development 3 days.
        InCommitted(People.Five)
            .Moved(Local(2026, 1, 9, 9), Workflow.Blocked)
            .Moved(Local(2026, 1, 13, 9), Workflow.Development)
            .Moved(Local(2026, 1, 16, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        // Committed: 1, 2, 3 and 4, median 2.5. Blocked: 1, 1 and 2, median 1. Development: 2, 1, 4, 1 and 3, median 2.
        // Nobody was in New, Approved, Review or QE.
        await Assert.That(TimeInStatusRows(await pipeline.ReadReportAsync(41))).IsEquivalentTo(
            new[] { "| Fake Committed | 4 | 2.5 |", "| Fake Blocked | 3 | 1.0 |", "| Fake Development | 5 | 2.0 |" }, CollectionOrdering.Matching);

        // The same figures from the metric view with ad hoc SQL.
        var rows = await pipeline.QueryAsync<(string StatusId, long IssueCount, double MedianBusinessDays)>(
            "SELECT status_id, issue_count, ROUND(median_business_days, 6) FROM time_in_status_per_sprint WHERE sprint_id = '41' ORDER BY board_position;");
        await Assert.That(rows).IsEquivalentTo(
            new[] { (Workflow.Committed.Id, 4L, 2.5), (Workflow.Blocked.Id, 3L, 1.0), (Workflow.Development.Id, 5L, 2.0) }, CollectionOrdering.Matching);

        FakeIssue InCommitted(FakePerson person) =>
            pipeline.Jira.AddIssue($"FAKE-{person.AccountId[^1]}", Local(2026, 1, 2, 9), createdIn: Workflow.Committed).InSprints(41).AssignedTo(person);
    }

    [Test]
    public async Task An_issue_that_spent_no_business_time_in_a_status_does_not_count_toward_it()
    {
        // Owner decision D21. Without it the weekend-only issue would make the Review row "4 issues".
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        // In Review only from Saturday 09:00 to Sunday 09:00: a calendar day, and no business time.
        // Development before that: 15 hours of Monday and Tuesday to Friday, 4.625 business days.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9), createdIn: Workflow.Development).InSprints(41).AssignedTo(People.One)
            .Moved(Local(2026, 1, 10, 9), Workflow.Review)
            .Moved(Local(2026, 1, 11, 9), Workflow.Done);

        // Three people's issues: Development from the sprint's start to Tuesday 09:00 (1 day), Review to Wednesday 09:00 (1 day).
        foreach (var (key, person) in new[] { ("FAKE-2", People.Two), ("FAKE-3", People.Three), ("FAKE-4", People.Four) })
        {
            pipeline.Jira.AddIssue(key, Local(2026, 1, 2, 9), createdIn: Workflow.Development).InSprints(41).AssignedTo(person)
                .Moved(Local(2026, 1, 6, 9), Workflow.Review)
                .Moved(Local(2026, 1, 7, 9), Workflow.Done);
        }

        // The fifth person: Development to Wednesday 09:00 (2 days), never in Review.
        pipeline.Jira.AddIssue("FAKE-5", Local(2026, 1, 2, 9), createdIn: Workflow.Development).InSprints(41).AssignedTo(People.Five)
            .Moved(Local(2026, 1, 7, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        // Development: 4.625, 1, 1, 1 and 2, median 1. Review: the three issues that were there on a business day.
        await Assert.That(TimeInStatusRows(await pipeline.ReadReportAsync(41))).IsEquivalentTo(
            new[] { "| Fake Development | 5 | 1.0 |", "| Fake Review | 3 | 1.0 |" }, CollectionOrdering.Matching);
        var rows = await pipeline.QueryAsync<(string StatusId, long IssueCount, double MedianBusinessDays)>(
            "SELECT status_id, issue_count, ROUND(median_business_days, 6) FROM time_in_status_per_sprint WHERE sprint_id = '41' ORDER BY board_position;");
        await Assert.That(rows).IsEquivalentTo(
            new[] { (Workflow.Development.Id, 5L, 1.0), (Workflow.Review.Id, 3L, 1.0) }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Time_in_shipped_is_stored_in_status_intervals_and_done_statuses_are_not_in_the_table()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.ServeEachIssueOncePer(People.Team);
        var issue = pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9)).InSprints(41)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 7, 9), Workflow.Done)
            .Moved(Local(2026, 1, 8, 9), Workflow.Shipped);

        await pipeline.RunAsync("run", "--sprint", "41");

        // Stored times are UTC; the team's zone is six hours behind it in January.
        var intervals = await pipeline.QueryAsync<(string StatusId, string Started, string? Ended)>(
            "SELECT status_id, started, ended FROM status_interval WHERE issue_id = @issueId AND status_id IN @done ORDER BY started;",
            new { issueId = issue.Id, done = new[] { Workflow.Done.Id, Workflow.Shipped.Id } });
        await Assert.That(intervals).IsEquivalentTo(
            new (string, string, string?)[]
            {
                (Workflow.Done.Id, "2026-01-07T15:00:00.000Z", "2026-01-08T15:00:00.000Z"),
                (Workflow.Shipped.Id, "2026-01-08T15:00:00.000Z", null),
            },
            CollectionOrdering.Matching);

        // Done and Shipped are not waiting on the team, so the table leaves them out.
        await Assert.That(TimeInStatusRows(await pipeline.ReadReportAsync(41))).IsEquivalentTo(
            new[] { "| Fake New | 5 | 1.0 |", "| Fake Development | 5 | 1.0 |" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_sprint_no_issue_belonged_to_says_so_in_place_of_the_table()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        // In sprint 41 only: sprint 42 has no member.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9), createdIn: Workflow.Development).InSprints(41);

        // Sprint 42 still has a team of five, or its table would read "suppressed" (owner decision D45): five
        // issues delivered in its window that were never members of it, so none spent time in it.
        foreach (var (person, number) in People.Team.Select((person, index) => (person, index + 2)))
        {
            pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 20, 9)).AssignedTo(person)
                .Moved(Local(2026, 1, 21, 9), Workflow.Done);
        }

        await pipeline.RunAsync("run", "--sprint", "42");

        var report = await pipeline.ReadReportAsync(42);
        await Assert.That(report).Contains("\n## Time in status\n\nNo issue of this sprint spent time in a status that is not done.\n");
        await Assert.That(TimeInStatusRows(report)).IsEmpty();
    }

    [Test]
    public async Task Calendar_time_business_time_and_business_days_are_stored_per_issue_sprint_and_status_without_weekends_and_holidays()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.ServeEachIssueOncePer(People.Team);
        pipeline.Config["holidays"] = new JsonArray("2026-01-12");

        // In Development from before the sprint until Tuesday 13 January 09:00, then Done to the end of the sprint.
        var issue = pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9), createdIn: Workflow.Development).InSprints(41)
            .Moved(Local(2026, 1, 13, 9), Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var rows = await pipeline.QueryAsync<(string IssueId, string SprintId, string StatusId, double CalendarSeconds, double BusinessSeconds, double BusinessDays)>(
            "SELECT issue_id, sprint_id, status_id, calendar_seconds, business_seconds, ROUND(business_days, 6) FROM sprint_status_time WHERE issue_id = @issueId ORDER BY status_id;",
            new { issueId = issue.Id });

        const double hour = 3600;
        await Assert.That(rows).IsEquivalentTo(
            new[]
            {
                // Monday 5 January 09:00 to Tuesday 13 January 09:00 is 8 calendar days. Business time: 15 hours of the
                // first Monday, Tuesday to Friday, nothing for the weekend or the holiday on Monday 12, 9 hours of Tuesday 13.
                (issue.Id, "41", Workflow.Development.Id, 8 * 24 * hour, (15 + 96 + 9) * hour, 5.0),

                // Tuesday 13 January 09:00 to the sprint's completion on Monday 19 January 09:00 is 6 calendar days.
                // Business time: 15 hours of Tuesday, Wednesday to Friday, 9 hours of Monday.
                (issue.Id, "41", Workflow.Done.Id, 6 * 24 * hour, (15 + 72 + 9) * hour, 4.0),
            },
            CollectionOrdering.Matching);
        await Assert.That(TimeInStatusRows(await pipeline.ReadReportAsync(41))).IsEquivalentTo(
            new[] { "| Fake Development | 5 | 5.0 |" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_configuration_file_without_the_holiday_list_is_refused_and_the_setting_is_named()
    {
        // The holiday list is required (ADR-0001): left out, every business-day figure would silently count holidays.
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Config.Remove("holidays");

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json is not valid").And.Contains("holidays");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    [Arguments("")]
    [Arguments("  ")]
    public async Task An_empty_sprint_field_id_is_refused(string fieldId)
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Config["jira"]!["sprintFieldId"] = fieldId;

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json is not valid: jira.sprintFieldId must not be empty.");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    [Arguments("status")]
    [Arguments("Sprint")]
    [Arguments("customfield_")]
    [Arguments("customfield_10020 ")]
    public async Task A_sprint_field_id_that_is_not_a_custom_field_id_is_refused(string fieldId)
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Config["jira"]!["sprintFieldId"] = fieldId;

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json is not valid: jira.sprintFieldId must be a custom field ID such as customfield_10020.");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    public async Task A_sprint_field_id_equal_to_the_story_point_field_id_is_refused()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Config["jira"]!["sprintFieldId"] = FakeJira.StoryPointFieldId;

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json is not valid: jira.sprintFieldId and jira.storyPointFieldId name the same field.");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    public async Task History_of_the_configured_sprint_field_that_does_not_hold_sprint_ids_stops_the_run_and_stores_nothing_of_it()
    {
        // sprintFieldId mistyped as a user picker: its history holds account IDs, which must never be stored.
        const string userPickerFieldId = "customfield_10099";
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Config["jira"]!["sprintFieldId"] = userPickerFieldId;
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9), createdIn: Workflow.Development)
            .AddHistory(Local(2026, 1, 7, 9), userPickerFieldId, "Fake Reviewer", People.One.AccountId, People.One.DisplayName, People.Two.AccountId, People.Two.DisplayName);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "The history of the field named by jira.sprintFieldId in the configuration file holds values that are not sprint IDs, so it is not the Sprint field. Nothing was stored.\n");
        var files = await pipeline.ReadAllFilesAsync();
        await Assert.That(files.Keys).Contains("store.db");
        var found = pipeline.Jira.ForbiddenText.Where(text => files.Values.Any(content => content.Contains(text, StringComparison.Ordinal))).ToList();
        await Assert.That(found).IsEmpty();
    }

    [Test]
    public async Task A_sprint_change_is_stored_in_the_form_the_sprint_field_is_stored_in()
    {
        // Jira writes a Sprint change as "41, 42". The store holds one form, "41,42", so the two columns compare.
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9), createdIn: Workflow.Development)
            .InSprints(41)
            .SprintsChanged(Local(2026, 1, 19, 10), 41, 42);

        await pipeline.RunAllAsync(["backfill"]);

        var changes = await pipeline.QueryAsync<(string From, string To)>("SELECT from_value, to_value FROM change WHERE field = 'sprint';");
        await Assert.That(changes).IsEquivalentTo(new[] { ("41", "41,42") });
        await Assert.That(await pipeline.QueryAsync<string>("SELECT sprint_ids FROM issue;")).IsEquivalentTo(new[] { "41,42" });
    }

    [Test]
    public async Task Sprint_changes_stored_as_jira_wrote_them_are_rewritten_in_the_stored_form_by_the_next_command()
    {
        // A store filled before Sprint changes were normalised holds "41, 42". A change is never written twice,
        // so nothing but a migration would ever correct it.
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9), createdIn: Workflow.Development)
            .InSprints(41)
            .SprintsChanged(Local(2026, 1, 19, 10), 41, 42);
        await pipeline.RunAllAsync(["backfill"]);
        await pipeline.ExecuteAsync(
            """
            UPDATE change SET from_value = ' 41', to_value = '41, 42' WHERE field = 'sprint';
            DELETE FROM schema_migration WHERE name = '0005_sprint_change_form.sql';
            """);

        await pipeline.RunAllAsync(["derive"]);

        var changes = await pipeline.QueryAsync<(string From, string To)>("SELECT from_value, to_value FROM change WHERE field = 'sprint';");
        await Assert.That(changes).IsEquivalentTo(new[] { ("41", "41,42") });
    }

    [Test]
    public async Task The_migration_of_sprint_changes_leaves_status_changes_and_empty_values_as_they_are()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9))
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .SprintsChanged(Local(2026, 1, 7, 9), 41)
            .SprintsChanged(Local(2026, 1, 8, 9));
        await pipeline.RunAllAsync(["backfill"]);
        var before = await pipeline.QueryAsync<(string Field, string From, string To)>("SELECT field, from_value, to_value FROM change ORDER BY history_id;");
        await pipeline.ExecuteAsync("DELETE FROM schema_migration WHERE name = '0005_sprint_change_form.sql';");

        await pipeline.RunAllAsync(["derive"]);

        var after = await pipeline.QueryAsync<(string Field, string From, string To)>("SELECT field, from_value, to_value FROM change ORDER BY history_id;");
        await Assert.That(before).IsEquivalentTo(new[] { ("status", Workflow.New.Id, Workflow.Development.Id), ("sprint", "", "41"), ("sprint", "41", "") }, CollectionOrdering.Matching);
        await Assert.That(after).IsEquivalentTo(before, CollectionOrdering.Matching);
    }

    [Test]
    public async Task White_space_of_any_kind_in_a_sprint_change_is_not_stored()
    {
        // A no-break space and a tab around the IDs: accepted as before, and gone from what is stored.
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9), createdIn: Workflow.Development)
            .AddHistory(Local(2026, 1, 7, 9), FakeJira.SprintFieldId, "Sprint", "", "", "41,\u00A042\t", "Fake Sprint 41, Fake Sprint 42")
            .InSprints(41, 42);

        await pipeline.RunAllAsync(["backfill"]);

        var changes = await pipeline.QueryAsync<(string From, string To)>("SELECT from_value, to_value FROM change WHERE field = 'sprint';");
        await Assert.That(changes).IsEquivalentTo(new[] { ("", "41,42") });
    }

    [Test]
    public async Task An_issue_stored_before_the_sprint_field_was_kept_stops_derive_with_a_message_to_backfill()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        // FAKE-1 is in no sprint, which is known and stops nothing.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9), createdIn: Workflow.Development);
        await Assert.That(await pipeline.RunAsync("run", "--sprint", "41")).IsEqualTo(0);

        // A row as an earlier version of the tool left it: no Sprint field value was ever fetched for it,
        // and Jira no longer returns the issue, so the sync does not fill it in.
        await pipeline.ExecuteAsync(
            $"INSERT INTO issue (issue_id, issue_key, issue_type_id, created, status_id, updated) VALUES ('900900', 'FAKE-900', '{FakeJira.IssueTypeStoryId}', '2026-01-02T15:00:00.000Z', '{Workflow.Development.Id}', '2026-01-02T15:00:00.000Z');");

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "1 issue in the store was fetched before the Sprint field was kept, so the sprints it belonged to are unknown. Run a backfill to fetch it again. No figures were derived.\n");
    }

    [Test]
    public async Task A_status_name_with_a_table_separator_stays_inside_its_cell()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.ServeEachIssueOncePer(People.Team);
        pipeline.Jira.RenameStatus(Workflow.Development, "Fake Dev | Build");
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9), createdIn: Workflow.Development).InSprints(41)
            .Moved(Local(2026, 1, 7, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(TimeInStatusRows(await pipeline.ReadReportAsync(41))).IsEquivalentTo(
            new[] { @"| Fake Dev \| Build | 5 | 2.0 |" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_report_from_derived_data_built_before_the_time_in_status_view_existed_says_to_rebuild_it()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9), createdIn: Workflow.Development).InSprints(41);
        await pipeline.RunAsync("run", "--sprint", "41");
        File.Delete(Path.Combine(pipeline.ReportDirectory, "sprint-41.md"));

        // The store as an earlier version of the tool derived it: no time in status view, and no record of
        // the raw data the derived tables were built from.
        await pipeline.ExecuteAsync("DROP VIEW time_in_status_per_sprint; DROP TABLE run; DELETE FROM derived_object WHERE name IN ('time_in_status_per_sprint', 'run');");

        var exit = await pipeline.RunAsync("report", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("The store has no derived data yet. Run derive first.\n");
        await Assert.That(File.Exists(Path.Combine(pipeline.ReportDirectory, "sprint-41.md"))).IsFalse();
    }

    [Test]
    public async Task No_personal_or_free_text_from_the_sprint_field_or_its_history_reaches_the_store_or_the_report()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddTeam(41);
        var issue = pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9), createdIn: Workflow.Development).AssignedTo(People.One);
        issue.AddHistory(Local(2026, 1, 7, 9), FakeJira.SprintFieldId, "Sprint", "", "", "41", "Fake Sprint 41", author: People.Two);
        issue.InSprints(41);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        var files = await pipeline.ReadAllFilesAsync();

        // The scan means something only if the sprint data went through: the report has the status's row.
        // The sprint has a team, so its rows are shown (owner decision D45); one person's row keeps its count
        // and withholds its figure.
        await Assert.That(files[Path.Combine("reports", "sprint-41.md")]).Contains("| Fake Development | 1 | suppressed |");
        await Assert.That(pipeline.Jira.ForbiddenText).Contains(People.Two.AccountId).And.Contains("Synthetic goal of sprint 41 that must never be stored");
        var found = pipeline.Jira.ForbiddenText.Where(text => files.Values.Any(content => content.Contains(text, StringComparison.Ordinal))).ToList();
        await Assert.That(found).IsEmpty();
    }

    /// <summary>The rows of the report's time in status table, without its two header lines. Empty when the report has no table.</summary>
    private static List<string> TimeInStatusRows(string report)
    {
        const string heading = "\n## Time in status\n\n";
        var start = report.IndexOf(heading, StringComparison.Ordinal);
        if (start < 0)
        {
            return [];
        }

        return [.. report[(start + heading.Length)..].Split('\n').TakeWhile(line => line.StartsWith('|')).Skip(2)];
    }

    /// <summary>
    /// Sprint 41: Monday 5 January 2026 09:00 to Monday 19 January 09:00, completed.
    /// Sprint 42: Monday 19 January 12:00 to Monday 2 February 09:00, completed.
    /// </summary>
    private static void AddTwoSprints(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 9));
        jira.AddSprint(42, "Fake Sprint 42", Local(2026, 1, 19, 12), plannedEnd: Local(2026, 2, 2, 9), completed: Local(2026, 2, 2, 9));
    }
}
