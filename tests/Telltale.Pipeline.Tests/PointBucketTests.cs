using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The point bucket is the estimate at the moment the cycle started (GLOSSARY: Point bucket; ADR-0006).
/// January 2026 for reading the dates: the 5th and 12th are Mondays.
/// </summary>
public class PointBucketTests
{
    private const string TableHeader =
        "\n## Cycle time by point bucket\n\n"
        + "| Point bucket | Issues with a cycle time | Median (business days) | P85 (business days) | Median (calendar days) | P85 (calendar days) |\n"
        + "| --- | ---: | ---: | ---: | ---: | ---: |\n";

    /// <summary>What follows the cycle time section. Asserting it after the last expected row shows the table has no further row.</summary>
    private const string NextSection = "\n## Time in status\n";

    private const string NotAnEstimate =
        "The history of the field named by jira.storyPointFieldId in the configuration file holds a value that is not a story point number this tool reads. "
        + "Either the setting names another field, or Jira writes the number in another form. Nothing was stored.\n";

    private const string HistoryNotCovered =
        "The store was last filled without the history of the fields that jira.sprintFieldId and jira.storyPointFieldId name now, "
        + "so sprint membership and point buckets could be wrong. Run backfill.\n";

    [Test]
    public async Task An_issue_re_estimated_after_its_cycle_started_stays_in_the_bucket_of_its_estimate_at_cycle_start()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);

        // Estimated 3 when development started on Tuesday; raised to 8 on Wednesday; done Thursday.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(3)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Reestimated(Local(2026, 1, 7, 9), 8)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(TableHeader + "| 3 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n" + NextSection);
    }

    [Test]
    public async Task An_issue_re_estimated_before_its_cycle_started_is_in_the_bucket_of_the_later_estimate()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);

        // Estimated 3 at creation; raised to 5 on Tuesday; development from Wednesday; done Friday.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(3)
            .Reestimated(Local(2026, 1, 6, 9), 5)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(TableHeader + "| 5 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n" + NextSection);
    }

    [Test]
    public async Task An_issue_with_no_estimate_at_cycle_start_is_unestimated_even_if_estimated_later()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);

        // No estimate when development started on Tuesday; estimated 5 on Wednesday; done Thursday.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9))
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Reestimated(Local(2026, 1, 7, 9), 5)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(TableHeader + "| unestimated | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n" + NextSection);
    }

    [Test]
    public async Task An_issue_with_no_point_history_uses_its_current_estimate()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(2)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(TableHeader + "| 2 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n" + NextSection);
    }

    [Test]
    public async Task Each_issue_is_in_the_bucket_of_its_own_estimate_at_its_own_cycle_start()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);

        // Both are estimated 8 now. FAKE-1 was 3 when it started on Tuesday; FAKE-2 was already 8 when it started on Thursday.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(3)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Reestimated(Local(2026, 1, 7, 9), 8)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 5, 9)).Estimated(3)
            .Reestimated(Local(2026, 1, 7, 9), 8)
            .Moved(Local(2026, 1, 8, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            TableHeader
            + "| 3 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "| 8 | 1 | 1.0 | 1.0 | 1.0 | 1.0 |\n"
            + NextSection);
    }

    [Test]
    public async Task Point_history_that_carries_its_numbers_in_from_and_to_is_read_the_same_way()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);

        // The other shape the documentation allows: the numbers are in from and to, and nowhere else. Raised from 3 to 5 before development started.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(5)
            .AddHistory(Local(2026, 1, 6, 9), FakeJira.StoryPointFieldId, "Fake Story Points", "3", null, "5.0", null)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .AddHistory(Local(2026, 1, 8, 9), FakeJira.StoryPointFieldId, "Fake Story Points", "5.0", null, "8", null)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(TableHeader + "| 5 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n" + NextSection);
    }

    [Test]
    public async Task A_re_estimate_fetched_by_a_later_sync_does_not_move_an_issue_that_had_already_started()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        var issue = pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(3)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);
        await pipeline.RunAsync("run", "--sprint", "41");

        // Before the second run the stored issue has no point history; after it, its current estimate is 8.
        issue.Reestimated(Local(2026, 1, 9, 9), 8);
        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(TableHeader + "| 3 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n" + NextSection);
    }

    [Test]
    [Arguments("fake-account-0001", "Fake Person One")]
    [Arguments(null, "Fake Person One")]
    [Arguments("NaN", "NaN")]
    [Arguments(null, "NaN")]
    [Arguments(null, "Infinity")]
    [Arguments("10001", "Fake Large")]
    [Arguments("3", "5")]
    [Arguments(null, "-3")]
    [Arguments("-3", null)]
    [Arguments(null, "1,5")]
    public async Task History_of_the_configured_story_point_field_that_does_not_hold_estimates_stops_the_run_and_stores_nothing_of_it(string? to, string? toText)
    {
        // storyPointFieldId mistyped as another field: its history holds account IDs, names or option IDs, which
        // must never be stored. A user picker, a text field, a select list (a numeric option ID beside its name),
        // two numbers that disagree, a negative number, and a number written with a decimal comma.
        const string otherFieldId = "customfield_10099";
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        pipeline.Config["jira"]!["storyPointFieldId"] = otherFieldId;
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9), createdIn: Workflow.Development)
            .AddHistory(Local(2026, 1, 7, 9), otherFieldId, "Fake Reviewer", null, null, to, toText);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(NotAnEstimate);
        var files = await pipeline.ReadAllFilesAsync();
        await Assert.That(files.Keys).Contains("store.db");
        var found = pipeline.Jira.ForbiddenText.Where(text => files.Values.Any(content => content.Contains(text, StringComparison.Ordinal))).ToList();
        await Assert.That(found).IsEmpty();
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM change;")).IsEquivalentTo(new[] { 0L });
    }

    [Test]
    public async Task Point_history_that_carries_the_same_number_in_both_places_is_read()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);

        // Raised from 3 to 5 before development started; each number is in from or to and again, written another way, in the text beside it.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(5)
            .AddHistory(Local(2026, 1, 6, 9), FakeJira.StoryPointFieldId, "Fake Story Points", "3.0", "3", "5.0", "5")
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(TableHeader + "| 5 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n" + NextSection);
    }

    [Test]
    public async Task Point_history_whose_values_are_json_numbers_is_read()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        pipeline.Jira.PointHistoryAsNumbers = true;

        // Estimated 0.5 when development started on Tuesday; raised to 8 on Wednesday; done Thursday.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(0.5)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Reestimated(Local(2026, 1, 7, 9), 8)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(TableHeader + "| 0.5 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n" + NextSection);
    }

    [Test]
    public async Task A_re_estimate_at_the_instant_of_the_cycle_start_but_recorded_after_the_move_into_work_does_not_move_the_issue()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);

        // Tuesday 09:00: moved into development, and then, in a later changelog entry with the same time, raised from 3 to 8.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(3)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Reestimated(Local(2026, 1, 6, 9), 8)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(TableHeader + "| 3 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n" + NextSection);
    }

    [Test]
    public async Task A_re_estimate_at_the_instant_of_the_cycle_start_recorded_before_the_move_into_work_is_the_issues_bucket()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);

        // Tuesday 09:00: raised from 3 to 8, and then, in a later changelog entry with the same time, moved into development.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(3)
            .Reestimated(Local(2026, 1, 6, 9), 8)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(TableHeader + "| 8 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n" + NextSection);
    }

    [Test]
    [Arguments("sprintFieldId")]
    [Arguments("storyPointFieldId")]
    public async Task A_sync_is_refused_when_a_field_whose_history_is_fetched_changed_since_the_store_was_filled(string setting)
    {
        // An issue the store already holds is fetched again only when it changes in Jira, so the history of a
        // newly named field would be missing for every other issue, and "no history" would be read as "never changed".
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(3)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);
        await pipeline.RunAllAsync(["backfill"]);
        var requestsBefore = pipeline.Jira.Handler.Requests.Count;

        pipeline.Config["jira"]![setting] = "customfield_10098";
        var sync = await pipeline.RunAsync("sync");
        var run = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(sync).IsEqualTo(1);
        await Assert.That(run).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(HistoryNotCovered + HistoryNotCovered);
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(requestsBefore);
    }

    [Test]
    public async Task A_sync_is_refused_on_a_store_filled_before_point_history_was_fetched()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(3)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);
        await pipeline.RunAllAsync(["backfill"]);

        // Such a store's watermark names the board, the projects and the start date, and no history fields.
        await pipeline.ExecuteAsync("UPDATE watermark SET covers = substr(covers, 1, instr(covers, '; history ') - 1);");
        var sync = await pipeline.RunAsync("sync");

        await Assert.That(sync).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(HistoryNotCovered);
    }

    [Test]
    public async Task After_a_backfill_under_the_changed_history_field_sync_works_again()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        var issue = pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(3)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development);
        await pipeline.RunAllAsync(["backfill"]);
        pipeline.Config["jira"]!["sprintFieldId"] = "customfield_10098";

        await pipeline.RunAllAsync(["backfill"]);
        issue.Moved(Local(2026, 1, 8, 9), Workflow.Done);
        await pipeline.RunAllAsync(["sync"], ["derive"], ["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(TableHeader + "| 3 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n" + NextSection);
    }

    [Test]
    public async Task Nothing_but_numbers_is_stored_from_story_point_history()
    {
        // Owner decision: story point history values are numbers only. Each is kept in one form however Jira
        // writes it ("0.50", " 3.0 "), and an empty value is no estimate. The raw table is read because no view shows it.
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9))
            .Reestimated(Local(2026, 1, 6, 9), 0.5)
            .AddHistory(Local(2026, 1, 7, 9), FakeJira.StoryPointFieldId, "Fake Story Points", null, "0.50", null, " 3.0 ")
            .AddHistory(Local(2026, 1, 8, 9), FakeJira.StoryPointFieldId, "Fake Story Points", null, "3.0", "", "");

        await pipeline.RunAsync("run", "--sprint", "41");

        var stored = await pipeline.QueryAsync<(string? From, string? To)>("SELECT from_value, to_value FROM change WHERE field = 'points' ORDER BY at;");
        await Assert.That(stored).IsEquivalentTo(new (string?, string?)[] { (null, "0.5"), ("0.5", "3"), ("3", null) }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var files = await pipeline.ReadAllFilesAsync();
        var found = pipeline.Jira.ForbiddenText.Where(text => files.Values.Any(content => content.Contains(text, StringComparison.Ordinal))).ToList();
        await Assert.That(found).IsEmpty();
    }

    [Test]
    public async Task A_delivered_issue_that_never_entered_a_work_status_is_in_the_bucket_of_its_estimate_at_the_delivery_moment()
    {
        // Owner decision D35. Such an issue is in no row of the cycle time table, so its bucket is read from derived data.
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);

        // Straight to done on Thursday. Raised from 3 to 5 on Tuesday, before that, and to 8 on Friday, after it.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(3)
            .Reestimated(Local(2026, 1, 6, 9), 5)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done)
            .Reestimated(Local(2026, 1, 9, 9), 8);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await BucketsOfDeliveredIssuesWithoutACycleAsync(pipeline)).IsEquivalentTo(new double?[] { 5 });
    }

    [Test]
    public async Task A_delivered_issue_that_never_entered_a_work_status_and_had_no_estimate_when_delivered_is_unestimated()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);

        // Straight to done on Thursday with no estimate; estimated 5 on Friday.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9))
            .Moved(Local(2026, 1, 8, 9), Workflow.Done)
            .Reestimated(Local(2026, 1, 9, 9), 5);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await BucketsOfDeliveredIssuesWithoutACycleAsync(pipeline)).IsEquivalentTo(new double?[] { null });
    }

    [Test]
    public async Task For_an_issue_that_never_entered_a_work_status_a_re_estimate_recorded_after_the_move_into_done_at_the_same_instant_does_not_count()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);

        // Thursday 09:00: moved straight to done, and then, in a later changelog entry with the same time, raised from 3 to 8.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(3)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done)
            .Reestimated(Local(2026, 1, 8, 9), 8);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await BucketsOfDeliveredIssuesWithoutACycleAsync(pipeline)).IsEquivalentTo(new double?[] { 3 });
    }

    [Test]
    public async Task For_an_issue_that_never_entered_a_work_status_a_re_estimate_recorded_before_the_move_into_done_at_the_same_instant_counts()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);

        // Thursday 09:00: raised from 3 to 5, and then, in a later changelog entry with the same time, moved straight to done. Raised to 8 on Friday.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(3)
            .Reestimated(Local(2026, 1, 8, 9), 5)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done)
            .Reestimated(Local(2026, 1, 9, 9), 8);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await BucketsOfDeliveredIssuesWithoutACycleAsync(pipeline)).IsEquivalentTo(new double?[] { 5 });
    }

    /// <summary>The bucket of every delivered issue that has no cycle. No metric view shows these issues' buckets.</summary>
    private static Task<IReadOnlyList<double?>> BucketsOfDeliveredIssuesWithoutACycleAsync(PipelineHarness pipeline) =>
        pipeline.QueryAsync<double?>("SELECT point_bucket FROM issue_cycle WHERE delivered = 1 AND cycle_start IS NULL;");

    private static void AddSprint(FakeJira jira) =>
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 20), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
}
