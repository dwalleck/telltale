using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary><c>derive</c> and <c>report</c> on their own, and which sprint a report is for when none is named.</summary>
public class SeparateCommandsTests
{
    [Test]
    public async Task Running_derive_twice_gives_identical_derived_data()
    {
        await using var pipeline = new PipelineHarness();
        AddBoard(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"]);
        var rawOnly = await pipeline.ReadStoreContentAsync();
        await pipeline.RunAllAsync(["derive"]);
        var afterFirst = await pipeline.ReadStoreContentAsync();

        await pipeline.RunAllAsync(["derive"]);

        await Assert.That(afterFirst).IsNotEqualTo(rawOnly);
        await Assert.That(await pipeline.ReadStoreContentAsync()).IsEqualTo(afterFirst);
    }

    [Test]
    public async Task A_completed_sprints_report_generated_twice_from_the_same_data_is_byte_for_byte_the_same()
    {
        await using var pipeline = new PipelineHarness();
        AddBoard(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "42"]);
        var first = await pipeline.ReadReportBytesAsync(42, withoutAsOf: true);

        // A day later, with the derived data rebuilt as well: nothing in a completed sprint's report depends on when
        // it was made, apart from the as-of time it prints (owner decision D33).
        pipeline.Clock.Advance(TimeSpan.FromDays(1));
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "42"]);

        await Assert.That(first).IsNotEmpty();
        await Assert.That(await pipeline.ReadReportBytesAsync(42, withoutAsOf: true)).IsEqualTo(first);
    }

    [Test]
    public async Task Report_without_a_sprint_reports_the_most_recently_completed_sprint()
    {
        await using var pipeline = new PipelineHarness();
        AddBoard(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        var exit = await pipeline.RunAsync("report");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(ReportFiles(pipeline)).IsEquivalentTo(new[] { "sprint-42.md" });
        await Assert.That(await pipeline.ReadReportAsync(42)).StartsWith("# Fake Sprint 42 (");
    }

    [Test]
    public async Task Report_with_a_sprint_id_reports_that_sprint()
    {
        await using var pipeline = new PipelineHarness();
        AddBoard(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        var exit = await pipeline.RunAsync("report", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(ReportFiles(pipeline)).IsEquivalentTo(new[] { "sprint-41.md" });
        await Assert.That(await pipeline.ReadReportAsync(41)).StartsWith("# Fake Sprint 41 (");
    }

    [Test]
    public async Task A_closed_sprint_without_a_completion_time_counts_as_completed_at_its_planned_end()
    {
        // Owner decision D7: such a sprint ended at its planned end, here later than sprint 41's completion.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddClosedSprintWithoutCompletion(45, "Fake Sprint 45", Local(2026, 1, 19, 10, 30), plannedEnd: Local(2026, 2, 2, 9));
        pipeline.Jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        var exit = await pipeline.RunAsync("report");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(ReportFiles(pipeline)).IsEquivalentTo(new[] { "sprint-45.md" });
    }

    [Test]
    public async Task Report_without_a_sprint_is_refused_when_no_sprint_has_completed()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint(43, "Fake Sprint 43", Local(2026, 2, 2, 10), plannedEnd: Local(2026, 2, 16, 9));
        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        var exit = await pipeline.RunAsync("report");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("No sprint in the store has completed yet. Name a sprint with --sprint.\n");
        await Assert.That(ReportFiles(pipeline)).IsEmpty();
    }

    [Test]
    public async Task Run_without_a_sprint_syncs_derives_and_reports_the_most_recently_completed_sprint()
    {
        await using var pipeline = new PipelineHarness();
        AddBoard(pipeline.Jira);

        var exit = await pipeline.RunAsync("run");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(ReportFiles(pipeline)).IsEquivalentTo(new[] { "sprint-42.md" });
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 4\n");
    }

    [Test]
    public async Task Run_without_a_sprint_when_none_has_completed_updates_the_store_says_so_and_succeeds()
    {
        // Owner decision D28: the routine command does not fail on a board in its first sprint.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint(43, "Fake Sprint 43", Local(2026, 2, 2, 10), plannedEnd: Local(2026, 2, 16, 9));
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 2, 2, 11))
            .Moved(Local(2026, 2, 2, 12), Workflow.Development)
            .Moved(Local(2026, 2, 3, 9), Workflow.Done);
        pipeline.Jira.AddTeam(43);

        var exit = await pipeline.RunAsync("run");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(pipeline.Out.ToString().ReplaceLineEndings("\n")).IsEqualTo("The store is up to date. No sprint has completed yet, so no report was written.\n");
        await Assert.That(ReportFiles(pipeline)).IsEmpty();

        // The store was synced and derived: the running sprint's report can be written from it as it is.
        await pipeline.RunAllAsync(["report", "--sprint", "43"]);
        await Assert.That(await pipeline.ReadReportAsync(43)).Contains("\nDelivered issues: 4\n");
    }

    [Test]
    public async Task Report_before_any_derive_is_refused_with_a_plain_message()
    {
        await using var pipeline = new PipelineHarness();
        AddBoard(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"]);

        var exit = await pipeline.RunAsync("report", "--sprint", "42");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("The store has no derived data yet. Run derive first.\n");
        await Assert.That(ReportFiles(pipeline)).IsEmpty();
    }

    [Test]
    public async Task Report_after_a_sync_that_changed_the_raw_data_is_refused_until_derive_runs_again()
    {
        await using var pipeline = new PipelineHarness();
        AddBoard(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "43"]);
        var reportBefore = await pipeline.ReadReportBytesAsync(43);

        // FAKE-3 is finished in the running sprint 43.
        pipeline.Jira.Issues[2].Moved(Local(2026, 2, 3, 9), Workflow.Done);
        await pipeline.RunAllAsync(["sync"]);
        var stale = await pipeline.RunAsync("report", "--sprint", "43");
        var staleReport = await pipeline.ReadReportBytesAsync(43);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "43"]);

        await Assert.That(stale).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("The raw data changed since the derived data was built. Run derive first.\n");
        await Assert.That(staleReport).IsEqualTo(reportBefore);
        await Assert.That(await pipeline.ReadReportAsync(43)).Contains("\nDelivered issues: 4\n");
    }

    [Test]
    public async Task Report_after_a_sync_that_removed_an_issue_converted_out_of_scope_is_refused_until_derive_runs_again()
    {
        // Owner decision D13 deletes the issue's rows from the raw tables; that is a change to the raw data too.
        await using var pipeline = new PipelineHarness();
        AddBoard(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        pipeline.Jira.Issues[2].OfType(FakeIssueType.Epic);
        await pipeline.RunAllAsync(["sync"]);
        var stale = await pipeline.RunAsync("report", "--sprint", "42");

        await Assert.That(stale).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("The raw data changed since the derived data was built. Run derive first.\n");
        await Assert.That(await pipeline.ReadStoreContentAsync()).DoesNotContain("FAKE-3");
    }

    [Test]
    public async Task Report_after_a_sync_that_changed_nothing_needs_no_new_derive()
    {
        await using var pipeline = new PipelineHarness();
        AddBoard(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        await pipeline.RunAllAsync(["sync"]);
        var exit = await pipeline.RunAsync("report", "--sprint", "42");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 4\n");
    }

    [Test]
    [Arguments("derive")]
    [Arguments("report")]
    public async Task A_command_that_only_reads_the_store_is_refused_before_any_backfill_and_creates_no_database(string command)
    {
        await using var pipeline = new PipelineHarness();

        var exit = await pipeline.RunAsync(command);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo($"There is no database at {pipeline.DatabasePath} yet. Run backfill first.\n");
        await Assert.That((await pipeline.ReadAllFilesAsync()).Keys).IsEquivalentTo(new[] { "telltale.json" });
    }

    [Test]
    public async Task A_command_started_while_another_is_using_the_store_is_refused_and_the_first_one_finishes()
    {
        await using var pipeline = new PipelineHarness();
        AddBoard(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"]);

        // While the sync waits for Jira's first answer, a second command starts on the same store.
        int? second = null;
        pipeline.Jira.BeforeResponse = () => second ??= pipeline.RunAsync("derive").GetAwaiter().GetResult();
        var first = await pipeline.RunAsync("sync");

        await Assert.That(second).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo($"Another Telltale command is using the database {pipeline.DatabasePath}. Wait for it to finish, then run this one again.\n");
        await Assert.That(first).IsEqualTo(0);

        // The store is free again once the first command has ended.
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "42"]);
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 4\n");
    }

    [Test]
    public async Task Derive_and_report_need_no_secrets_and_never_call_jira()
    {
        await using var pipeline = new PipelineHarness();
        AddBoard(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"]);
        var requestsDuringBackfill = pipeline.Jira.Handler.Requests.Count;
        pipeline.Secrets.Clear();

        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "42"]);

        await Assert.That(requestsDuringBackfill).IsGreaterThan(0);
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(requestsDuringBackfill);
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 4\n");
    }

    private static string[] ReportFiles(PipelineHarness pipeline) =>
        Directory.Exists(pipeline.ReportDirectory)
            ? [.. Directory.EnumerateFiles(pipeline.ReportDirectory).Select(path => Path.GetFileName(path)!)]
            : [];

    /// <summary>
    /// A board as it looks on 3 February 2026: sprint 42 completed the day before, sprint 43 running, sprint 44
    /// not started, and the older sprints 40 and 41, listed out of order. One issue was delivered in each of 41 and 42.
    /// </summary>
    private static void AddBoard(FakeJira jira)
    {
        jira.AddSprint(43, "Fake Sprint 43", Local(2026, 2, 2, 10), plannedEnd: Local(2026, 2, 16, 9));
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        jira.AddSprint(42, "Fake Sprint 42", Local(2026, 1, 19, 10, 30), plannedEnd: Local(2026, 2, 2, 9), completed: Local(2026, 2, 2, 9, 30));
        jira.AddFutureSprint(44, "Fake Sprint 44", Local(2026, 2, 16, 10), Local(2026, 3, 2, 9));
        jira.AddSprint(40, "Fake Sprint 40", Local(2025, 12, 22, 9), plannedEnd: Local(2026, 1, 5, 8), completed: Local(2026, 1, 5, 8, 30));

        jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).AssignedTo(People.One)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 15), Workflow.Done);
        jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9)).AssignedTo(People.Two)
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        jira.AddIssue("FAKE-3", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 2, 2, 11), Workflow.Development);
        // A team of five in each sprint, so its delivered count is shown (SuppressionTests).
        jira.AddTeam(41, 42, 43);
    }
}
