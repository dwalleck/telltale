using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>What a real Jira Cloud site does at scale (ADR-0004): pages, repeated history entries, rate limits, old history.</summary>
public class RealJiraCollectionTests
{
    [Test]
    [Arguments(JiraShapes.AsSchemas)]
    [Arguments(JiraShapes.AsExamples)]
    public async Task Issues_spread_over_several_search_pages_are_all_collected(JiraShapes shapes)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Shapes = shapes;
        pipeline.Jira.SearchPageSize = 2;
        AddSprint41(pipeline.Jira);
        AddDeliveredIssues(pipeline.Jira, count: 5);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 5\n");
        await Assert.That(pipeline.Jira.Handler.Requests.Count(request => request.Path == "/rest/api/3/search/jql")).IsEqualTo(3);
    }

    [Test]
    public async Task An_issue_met_again_on_a_later_search_page_is_stored_once_and_its_history_is_asked_for_once()
    {
        // The search is ordered by update time, so an issue updated while it is being paged comes round again.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.SearchPageSize = 2;
        pipeline.Jira.RepeatsIssueOnNextPage = true;
        AddSprint41(pipeline.Jira);
        AddDeliveredIssues(pipeline.Jira, count: 3);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(pipeline.Jira.SearchAnswers[0]).IsEquivalentTo(new[] { "FAKE-1", "FAKE-2", "FAKE-2", "FAKE-3" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var changelogRequest = pipeline.Jira.Handler.Requests.Single(request => request.Path == "/rest/api/3/changelog/bulkfetch");
        using var body = System.Text.Json.JsonDocument.Parse(changelogRequest.Body);
        var askedFor = body.RootElement.GetProperty("issueIdsOrKeys").EnumerateArray().Select(id => id.GetString()!).ToList();
        await Assert.That(askedFor).IsEquivalentTo(pipeline.Jira.Issues.Select(issue => issue.Id));
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 3\n");
    }

    [Test]
    public async Task Epics_and_sub_tasks_that_fill_search_pages_do_not_stop_the_run_or_hide_the_issues_after_them()
    {
        // Ticket 02's review, finding 5: out-of-scope issues take room on a page. The first page holds none but them.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.SearchPageSize = 2;
        AddSprint41(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-101", Local(2026, 1, 5, 10)).OfType(FakeIssueType.Epic);
        pipeline.Jira.AddIssue("FAKE-102", Local(2026, 1, 5, 11)).OfType(FakeIssueType.SubTask);
        AddDeliveredIssues(pipeline.Jira, count: 2);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(pipeline.Jira.SearchAnswers[0]).IsEquivalentTo(new[] { "FAKE-101", "FAKE-102", "FAKE-1", "FAKE-2" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 2\n");
    }

    [Test]
    [Arguments(JiraShapes.AsSchemas)]
    [Arguments(JiraShapes.AsExamples)]
    public async Task Changelogs_spread_over_several_pages_are_all_collected(JiraShapes shapes)
    {
        // Three issues with two moves each: six history entries, two to a page. The entries come oldest first,
        // so each issue's moves are on different pages.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Shapes = shapes;
        pipeline.Jira.ChangelogPageSize = 2;
        AddSprint41(pipeline.Jira);
        AddDeliveredIssues(pipeline.Jira, count: 3);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 3\n");
        await Assert.That(pipeline.Jira.Handler.Requests.Count(request => request.Path == "/rest/api/3/changelog/bulkfetch")).IsEqualTo(3);
    }

    [Test]
    public async Task The_same_history_entry_returned_on_two_pages_is_stored_once_and_changes_no_figure()
    {
        // ADR-0004. The same site twice: once every changelog page after the first repeats the entry before it, once not.
        await using var repeating = new PipelineHarness();
        await using var plain = new PipelineHarness();
        foreach (var pipeline in new[] { repeating, plain })
        {
            pipeline.Jira.ChangelogPageSize = 2;
            AddSprint41(pipeline.Jira);
            AddDeliveredIssues(pipeline.Jira, count: 3);
        }

        repeating.Jira.RepeatsHistoryOnNextPage = true;

        await repeating.RunAllAsync(["run", "--sprint", "41"]);
        await plain.RunAllAsync(["run", "--sprint", "41"]);

        // Six moves, each stored once (the raw table is what "stored once" is about).
        await Assert.That(await repeating.QueryAsync<long>("SELECT COUNT(*) FROM change;")).IsEquivalentTo(new[] { 6L });
        await Assert.That(await repeating.ReadStoreContentAsync()).IsEqualTo(await plain.ReadStoreContentAsync());
        await Assert.That(await repeating.ReadReportBytesAsync(41)).IsEqualTo(await plain.ReadReportBytesAsync(41));
        await Assert.That(await plain.ReadReportAsync(41)).Contains("\nDelivered issues: 3\n");
    }

    [Test]
    [Arguments(SprintListEnd.IsLastAndTotal)]
    [Arguments(SprintListEnd.IsLastOnly)]
    [Arguments(SprintListEnd.TotalOnly)]
    [Arguments(SprintListEnd.Neither)]
    public async Task A_board_whose_sprints_span_several_pages_has_all_of_them_collected(SprintListEnd sprintListSays)
    {
        // Five closed sprints, two to a page, and an issue delivered in each of the first and the last.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.SprintListSays = sprintListSays;
        pipeline.Jira.SprintPageSize = 2;
        for (var sprint = 0; sprint < 5; sprint++)
        {
            var start = Local(2025, 11, 3, 9).AddDays(14 * sprint);
            pipeline.Jira.AddSprint(41 + sprint, $"Fake Sprint {41 + sprint}", start, plannedEnd: start.AddDays(14), completed: start.AddDays(14));
        }

        pipeline.Jira.AddIssue("FAKE-1", Local(2025, 11, 4, 9))
            .Moved(Local(2025, 11, 5, 9), Workflow.Development)
            .Moved(Local(2025, 11, 6, 9), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-2", Local(2025, 12, 30, 9))
            .Moved(Local(2025, 12, 31, 9), Workflow.Development)
            .Moved(Local(2026, 1, 2, 9), Workflow.Done);

        await pipeline.RunAllAsync(["run"]);

        var delivered = await pipeline.QueryAsync<(string SprintId, long DeliveredCount)>("SELECT sprint_id, delivered_count FROM delivered_per_sprint ORDER BY sprint_id;");
        await Assert.That(delivered).IsEquivalentTo(new[] { ("41", 1L), ("42", 0L), ("43", 0L), ("44", 0L), ("45", 1L) });
        await Assert.That(await pipeline.ReadReportAsync(45)).StartsWith("# Fake Sprint 45 ");
    }

    [Test]
    public async Task A_rate_limited_answer_followed_by_success_completes_the_run_after_waiting_as_long_as_jira_said()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41(pipeline.Jira);
        AddDeliveredIssues(pipeline.Jira, count: 1);
        pipeline.Jira.Handler.AnswerNext(HttpMethod.Post, "/rest/api/3/search/jql", StubHttpHandler.RateLimited(30), StubHttpHandler.RateLimited(7));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 1\n");

        // The waits went through the clock the command was given, so they took no real time and are on record.
        await Assert.That(pipeline.Clock.Waits).IsEquivalentTo(new[] { TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(7) }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Rate_limited_answers_beyond_the_retry_limit_fail_the_run_plainly_and_leave_the_store_and_the_watermark_as_they_were()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41(pipeline.Jira);
        AddDeliveredIssues(pipeline.Jira, count: 2);
        await pipeline.RunAllAsync(["backfill"]);
        var before = await pipeline.ReadStoreContentAsync();
        var waitsBefore = pipeline.Clock.Waits.Count;

        // Something to fetch, so a sync that got through would move the watermark.
        pipeline.Jira.Issues[0].Moved(Local(2026, 1, 12, 9), Workflow.Shipped);
        pipeline.Jira.Handler.Override(HttpMethod.Post, "/rest/api/3/changelog/bulkfetch", _ => true, StubHttpHandler.RateLimited(2));

        var exit = await pipeline.RunAsync("sync");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Jira is limiting how often it can be asked: it answered 429 (too many requests) to POST rest/api/3/changelog/bulkfetch again after 5 retries. Run the command again later.\n");
        await Assert.That(before).Contains("## watermark\njira | ");
        await Assert.That(await pipeline.ReadStoreContentAsync()).IsEqualTo(before);

        // Five retries, each after the wait Jira asked for; the sixth 429 ends the run without another wait.
        await Assert.That(pipeline.Clock.Waits.Skip(waitsBefore)).IsEquivalentTo(Enumerable.Repeat(TimeSpan.FromSeconds(2), 5));
        await Assert.That(pipeline.Jira.Handler.Requests.Count(request => request.Path == "/rest/api/3/changelog/bulkfetch")).IsEqualTo(1 + 6);
    }

    [Test]
    public async Task A_rate_limited_answer_that_names_no_wait_is_retried_after_waits_that_double()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41(pipeline.Jira);
        AddDeliveredIssues(pipeline.Jira, count: 1);
        pipeline.Jira.Handler.AnswerNext(HttpMethod.Get, "/rest/api/3/status", StubHttpHandler.RateLimited(null), StubHttpHandler.RateLimited(null), StubHttpHandler.RateLimited(null));

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(pipeline.Clock.Waits).IsEquivalentTo(new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20) }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 1\n");
    }

    [Test]
    public async Task A_rate_limited_answer_that_names_a_time_to_come_back_is_retried_at_that_time()
    {
        // The other documented form of Retry-After: an HTTP date instead of a number of seconds.
        await using var pipeline = new PipelineHarness();
        AddSprint41(pipeline.Jira);
        AddDeliveredIssues(pipeline.Jira, count: 1);
        var start = pipeline.Clock.GetUtcNow();
        pipeline.Jira.Handler.AnswerNext(HttpMethod.Get, $"/rest/agile/1.0/board/{FakeJira.BoardId}/sprint", StubHttpHandler.RateLimitedUntil(start.AddSeconds(90)));

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(pipeline.Clock.Waits).IsEquivalentTo(new[] { TimeSpan.FromSeconds(90) });
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 1\n");
    }

    [Test]
    [Arguments(0, 1, true)]
    [Arguments(300, 300, true)]
    [Arguments(301, 0, false)]
    public async Task A_rate_limited_answer_is_waited_out_for_at_least_a_second_and_at_most_five_minutes(int retryAfterSeconds, int secondsWaited, bool retried)
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41(pipeline.Jira);
        AddDeliveredIssues(pipeline.Jira, count: 1);
        pipeline.Jira.Handler.AnswerNext(HttpMethod.Post, "/rest/api/3/search/jql", StubHttpHandler.RateLimited(retryAfterSeconds));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(retried ? 0 : 1);
        await Assert.That(pipeline.Clock.Waits).IsEquivalentTo(retried ? new[] { TimeSpan.FromSeconds(secondsWaited) } : []);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(retried
            ? ""
            : "Jira is limiting how often it can be asked: it answered 429 (too many requests) to POST rest/api/3/search/jql and asked for a wait of more than 5 minutes. Run the command again later.\n");
    }

    [Test]
    [Arguments("/rest/api/3/search/jql", "Jira answered 500 (InternalServerError) to POST rest/api/3/search/jql.\n")]
    [Arguments("/rest/api/3/changelog/bulkfetch", "Jira answered 500 (InternalServerError) to POST rest/api/3/changelog/bulkfetch.\n")]
    public async Task A_backfill_that_fails_midway_and_is_run_again_ends_with_the_same_store_as_one_that_was_not_interrupted(string failingPath, string message)
    {
        // The same site twice, with two issues or history entries to a page. On one, the first backfill gets its
        // first page and then a refusal for the second.
        await using var interrupted = new PipelineHarness();
        await using var uninterrupted = new PipelineHarness();
        foreach (var pipeline in new[] { interrupted, uninterrupted })
        {
            pipeline.Jira.SearchPageSize = 2;
            pipeline.Jira.ChangelogPageSize = 2;
            AddSprint41(pipeline.Jira);
            AddDeliveredIssues(pipeline.Jira, count: 5);
        }

        var jiraIsFailing = true;
        interrupted.Jira.Handler.Override(HttpMethod.Post, failingPath, request => jiraIsFailing && request.Body.Contains("nextPageToken", StringComparison.Ordinal),
            StubHttpHandler.Json("{}", System.Net.HttpStatusCode.InternalServerError));
        var failed = await interrupted.RunAsync("backfill");
        var errorOfFailedRun = interrupted.Error.ToString().ReplaceLineEndings("\n");
        var storeAfterFailedRun = await interrupted.ReadStoreContentAsync();
        jiraIsFailing = false;

        await interrupted.RunAllAsync(["backfill"], ["derive"]);
        await uninterrupted.RunAllAsync(["backfill"], ["derive"]);

        await Assert.That(failed).IsEqualTo(1);
        await Assert.That(errorOfFailedRun).IsEqualTo(message);

        // The failed run kept nothing of the pages it did get, and no watermark.
        await Assert.That(storeAfterFailedRun).DoesNotContain("FAKE-").And.Contains("## watermark\n\n");
        await Assert.That(await uninterrupted.ReadStoreContentAsync()).Contains("FAKE-5");
        await Assert.That(await interrupted.ReadStoreContentAsync()).IsEqualTo(await uninterrupted.ReadStoreContentAsync());
    }

    [Test]
    public async Task Old_history_whose_items_carry_no_field_id_and_null_values_still_counts()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41(pipeline.Jira);

        // In Development since the Friday before the sprint; added to the sprint on Wednesday 09:00; Done on Friday 09:00.
        // Both changes are in the old form: the field's name only, and null where the issue was in no sprint.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9), createdIn: Workflow.Development)
            .SprintsChangedInOldHistory(Local(2026, 1, 7, 9), 41)
            .MovedInOldHistory(Local(2026, 1, 9, 9), Workflow.Done);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: 1\n");
        await Assert.That(report).Contains("\n| Fake Development | 1 | 2.0 |\n");
    }

    [Test]
    public async Task Old_history_of_a_custom_field_that_happens_to_be_named_status_is_not_taken_for_a_move()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41(pipeline.Jira);
        AddDeliveredIssues(pipeline.Jira, count: 1);
        pipeline.Jira.Issues[0].AddHistory(Local(2026, 1, 8, 9), "customfield_10999", "status", null, null, "700001", "Synthetic custom value", withoutFieldId: true);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        // Taken for a move, it would put the issue in a status that does not exist, and derive would stop.
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 1\n");
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM change;")).IsEquivalentTo(new[] { 2L });
    }

    private static void AddSprint41(FakeJira jira) =>
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));

    /// <summary>Issues FAKE-1 to FAKE-n, each created, started and finished inside sprint 41, an hour after the one before.</summary>
    private static void AddDeliveredIssues(FakeJira jira, int count)
    {
        for (var number = 1; number <= count; number++)
        {
            jira.AddIssue($"FAKE-{number}", Local(2026, 1, 6, 9))
                .Moved(Local(2026, 1, 7, 9 + number), Workflow.Development)
                .Moved(Local(2026, 1, 9, 9 + number), Workflow.Done);
        }
    }
}
