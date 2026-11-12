using System.Net;
using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// <c>backfill</c> rebuilds the raw tables: afterwards they hold what Jira returned to that backfill and
/// nothing else (owner decision D4). <c>sync</c> only adds and updates.
/// </summary>
public class BackfillRebuildTests
{
    [Test]
    public async Task An_issue_deleted_in_jira_is_still_counted_after_a_sync_and_no_longer_after_a_backfill()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        var deleted = pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 23, 9), Workflow.Done);
        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "42"]);
        var beforeTheDeletion = await pipeline.ReadReportAsync(42);

        pipeline.Jira.Delete(deleted);
        await pipeline.RunAllAsync(["sync"], ["derive"], ["report", "--sprint", "42"]);
        var afterSync = await pipeline.ReadReportAsync(42);
        var storeAfterSync = await pipeline.ReadStoreContentAsync();
        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "42"]);

        await Assert.That(beforeTheDeletion).Contains("\nDelivered issues: 2\n");
        await Assert.That(afterSync).Contains("\nDelivered issues: 2\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 1\n");

        // Not only uncounted: no row of the issue is left, raw or derived.
        await Assert.That(storeAfterSync).Contains("FAKE-2");
        await Assert.That(await pipeline.ReadStoreContentAsync()).DoesNotContain("FAKE-2");
    }

    [Test]
    public async Task A_sprint_removed_from_the_board_in_jira_stays_after_a_sync_and_is_gone_from_the_store_after_a_backfill()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        var removed = pipeline.Jira.AddSprint(42, "Fake Sprint 42", Local(2026, 1, 19, 10, 30), plannedEnd: Local(2026, 2, 2, 9), completed: Local(2026, 2, 2, 9, 30));
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        pipeline.Jira.RemoveSprint(removed);
        await pipeline.RunAllAsync(["sync"], ["derive"]);
        var afterSync = await pipeline.ReadStoreContentAsync();
        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        await Assert.That(afterSync).Contains("Fake Sprint 42");
        var afterBackfill = await pipeline.ReadStoreContentAsync();
        await Assert.That(afterBackfill).DoesNotContain("Fake Sprint 42");
        await Assert.That(afterBackfill).Contains("Fake Sprint 41");

        // The most recently completed sprint is now 41, so that is the one a report without --sprint is about.
        await pipeline.RunAllAsync(["report"]);
        await Assert.That(pipeline.Out.ToString()).Contains("sprint-41.md");
    }

    [Test]
    [Arguments("storyPointFieldId")]
    [Arguments("sprintFieldId")]
    public async Task A_backfill_after_a_history_field_setting_changed_leaves_no_history_fetched_under_the_old_one(string setting)
    {
        // The store after the second backfill is the store a first backfill under the new setting gives.
        await using var changed = new PipelineHarness();
        BuildBoard(changed.Jira);
        await changed.RunAllAsync(["backfill"], ["derive"]);
        var underTheOldSetting = await changed.ReadStoreContentAsync();
        changed.Config["jira"]![setting] = "customfield_10098";
        await changed.RunAllAsync(["backfill"], ["derive"]);

        await using var fresh = new PipelineHarness();
        BuildBoard(fresh.Jira);
        fresh.Config["jira"]![setting] = "customfield_10098";
        await fresh.RunAllAsync(["backfill"], ["derive"]);

        var store = await changed.ReadStoreContentAsync();
        await Assert.That(store).IsNotEqualTo(underTheOldSetting);
        await Assert.That(store).Contains("FAKE-1");
        await Assert.That(store).IsEqualTo(await fresh.ReadStoreContentAsync());

        static void BuildBoard(FakeJira jira)
        {
            AddTwoSprints(jira);
            jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).Estimated(3).InSprints(41)
                .Reestimated(Local(2026, 1, 7, 9), 8)
                .SprintsChanged(Local(2026, 1, 20, 9), 42)
                .Moved(Local(2026, 1, 21, 9), Workflow.Development)
                .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        }
    }

    [Test]
    public async Task A_status_deleted_in_jira_stays_after_a_sync_and_is_gone_from_the_store_after_a_backfill()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        await pipeline.RunAllAsync(["backfill"]);

        pipeline.Jira.DeleteStatus(Workflow.Shipped);
        await pipeline.RunAllAsync(["sync"]);
        var afterSync = await pipeline.ReadStoreContentAsync();
        await pipeline.RunAllAsync(["backfill"]);

        await Assert.That(afterSync).Contains("Fake Shipped");
        var afterBackfill = await pipeline.ReadStoreContentAsync();
        await Assert.That(afterBackfill).DoesNotContain("Fake Shipped");
        await Assert.That(afterBackfill).Contains("Fake Done");
    }

    [Test]
    [Arguments("the fetch", "Jira answered 500 (InternalServerError) to POST rest/api/3/changelog/bulkfetch.")]
    [Arguments("the write", "synthetic write failure")]
    public async Task A_backfill_that_fails_part_way_leaves_the_store_and_the_report_as_they_were_and_run_again_ends_as_an_uninterrupted_one(string whatFails, string failure)
    {
        await using var interrupted = new PipelineHarness();
        BuildBoard(interrupted.Jira);
        await interrupted.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "42"]);
        var storeBefore = await interrupted.ReadStoreContentAsync();
        var reportBefore = await interrupted.ReadReportBytesAsync(42);

        // Jira loses a delivered issue, and the backfill that would clear it out of the store fails: while
        // Jira is being read, or late in the write, after the old rows have been removed and the new ones added.
        ChangeBoard(interrupted.Jira);
        var failing = true;
        if (whatFails == "the fetch")
        {
            interrupted.Jira.Handler.Override(HttpMethod.Post, "/rest/api/3/changelog/bulkfetch", _ => failing, StubHttpHandler.Json("{}", HttpStatusCode.InternalServerError));
        }
        else
        {
            await interrupted.ExecuteAsync("CREATE TRIGGER synthetic_failure BEFORE INSERT ON watermark BEGIN SELECT RAISE(ABORT, 'synthetic write failure'); END;");
        }

        var failed = await interrupted.RunAsync("backfill");
        if (whatFails == "the write")
        {
            await interrupted.ExecuteAsync("DROP TRIGGER synthetic_failure;");
        }

        failing = false;
        var storeAfterFailure = await interrupted.ReadStoreContentAsync();
        await interrupted.RunAllAsync(["report", "--sprint", "42"]);
        var reportAfterFailure = await interrupted.ReadReportBytesAsync(42);
        await interrupted.RunAllAsync(["backfill"], ["derive"]);

        await using var uninterrupted = new PipelineHarness();
        BuildBoard(uninterrupted.Jira);
        ChangeBoard(uninterrupted.Jira);
        await uninterrupted.RunAllAsync(["backfill"], ["derive"]);

        await Assert.That(failed).IsEqualTo(1);
        await Assert.That(interrupted.Error.ToString()).Contains(failure);
        await Assert.That(storeBefore).Contains("FAKE-2");
        await Assert.That(storeAfterFailure).IsEqualTo(storeBefore);
        await Assert.That(reportAfterFailure).IsEqualTo(reportBefore);
        var store = await interrupted.ReadStoreContentAsync();
        await Assert.That(store).DoesNotContain("FAKE-2");
        await Assert.That(store).IsEqualTo(await uninterrupted.ReadStoreContentAsync());

        static void BuildBoard(FakeJira jira)
        {
            AddTwoSprints(jira);
            jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9)).AssignedTo(People.One)
                .Moved(Local(2026, 1, 21, 9), Workflow.Development);
            jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9)).AssignedTo(People.Two)
                .Moved(Local(2026, 1, 21, 9), Workflow.Development)
                .Moved(Local(2026, 1, 23, 9), Workflow.Done);
        }

        static void ChangeBoard(FakeJira jira)
        {
            jira.Issues[0].Moved(Local(2026, 1, 26, 9), Workflow.Done);
            jira.Delete(jira.Issues[1]);
        }
    }

    [Test]
    public async Task The_rows_of_an_issue_still_in_jira_are_the_same_before_and_after_a_backfill_that_clears_another()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        var kept = pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9)).AssignedTo(People.One).Estimated(3).InSprints(42)
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        var deleted = pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9)).AssignedTo(People.Two)
            .Moved(Local(2026, 1, 21, 9), Workflow.Development);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var before = RowsOf(kept, await pipeline.ReadStoreContentAsync());

        pipeline.Jira.Delete(deleted);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        // Every row that names the issue, raw and derived: its issue row with the hashed assignee, its changes,
        // its intervals and its cycle.
        var store = await pipeline.ReadStoreContentAsync();
        await Assert.That(before).Contains("FAKE-1");
        await Assert.That(before.Split('\n')).Count().IsGreaterThanOrEqualTo(4);
        await Assert.That(before).Matches("(?m)^.*FAKE-1.* \\| [0-9a-f]{64} \\| .*$");
        await Assert.That(RowsOf(kept, store)).IsEqualTo(before);
        await Assert.That(store).DoesNotContain("FAKE-2");

        static string RowsOf(FakeIssue issue, string store) =>
            string.Join('\n', store.Split('\n').Where(row => row.Contains(issue.Id, StringComparison.Ordinal)));
    }

    [Test]
    public async Task The_watermark_after_a_backfill_is_the_one_a_first_backfill_leaves_even_when_a_sync_had_reached_further()
    {
        // A sync reaches 30 January through an issue that is then deleted. The backfill's watermark is the
        // newest update of what Jira still holds, 22 January, and not the further one the sync had left.
        await using var pipeline = new PipelineHarness();
        BuildBoard(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"]);
        ChangeBoard(pipeline.Jira);
        await pipeline.RunAllAsync(["sync"]);
        var afterSync = Watermark(await pipeline.ReadStoreContentAsync());
        pipeline.Jira.Delete(pipeline.Jira.Issues[1]);
        await pipeline.RunAllAsync(["backfill"]);

        await using var first = new PipelineHarness();
        BuildBoard(first.Jira);
        ChangeBoard(first.Jira);
        first.Jira.Delete(first.Jira.Issues[1]);
        await first.RunAllAsync(["backfill"]);

        var watermark = Watermark(await pipeline.ReadStoreContentAsync());
        await Assert.That(afterSync).Contains("2026-01-30");
        await Assert.That(watermark).Contains("2026-01-22");
        await Assert.That(watermark).DoesNotContain("2026-01-30");
        await Assert.That(watermark).IsEqualTo(Watermark(await first.ReadStoreContentAsync()));

        static void BuildBoard(FakeJira jira)
        {
            AddTwoSprints(jira);
            jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9))
                .Moved(Local(2026, 1, 21, 9), Workflow.Development)
                .Moved(Local(2026, 1, 22, 9), Workflow.Done);
            jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9))
                .Moved(Local(2026, 1, 21, 9), Workflow.Development);
        }

        static void ChangeBoard(FakeJira jira) => jira.Issues[1].Moved(Local(2026, 1, 30, 9), Workflow.Done);

        // The watermark table's block of the store's content: its heading and its one row.
        static string Watermark(string store)
        {
            var start = store.IndexOf("## watermark\n", StringComparison.Ordinal);
            var end = store.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
            return end < 0 ? store[start..] : store[start..end];
        }
    }

    [Test]
    public async Task A_backfill_under_another_hashing_salt_leaves_no_person_hashed_with_the_old_one()
    {
        // FAKE-2 is not fetched again, so nothing would give its assignee a hash under the new salt.
        const string NewSalt = "synthetic-salt-two";
        await using var resalted = new PipelineHarness();
        BuildBoard(resalted.Jira);
        await resalted.RunAllAsync(["backfill"], ["derive"]);
        var underTheOldSalt = await resalted.ReadStoreContentAsync();
        resalted.Jira.Delete(resalted.Jira.Issues[1]);
        resalted.Secrets[SecretName.HashingSalt] = NewSalt;
        await resalted.RunAllAsync(["backfill"], ["derive"]);

        await using var fresh = new PipelineHarness();
        BuildBoard(fresh.Jira);
        fresh.Jira.Delete(fresh.Jira.Issues[1]);
        fresh.Secrets[SecretName.HashingSalt] = NewSalt;
        await fresh.RunAllAsync(["backfill"], ["derive"]);

        // The same board under the old salt, to show the comparison would see a hash left from it.
        await using var oldSalt = new PipelineHarness();
        BuildBoard(oldSalt.Jira);
        oldSalt.Jira.Delete(oldSalt.Jira.Issues[1]);
        await oldSalt.RunAllAsync(["backfill"], ["derive"]);

        var store = await resalted.ReadStoreContentAsync();
        await Assert.That(underTheOldSalt).Contains("FAKE-2");
        await Assert.That(store).IsNotEqualTo(await oldSalt.ReadStoreContentAsync());
        await Assert.That(store).IsEqualTo(await fresh.ReadStoreContentAsync());

        static void BuildBoard(FakeJira jira)
        {
            AddTwoSprints(jira);
            jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9)).AssignedTo(People.One)
                .Moved(Local(2026, 1, 21, 9), Workflow.Development)
                .Moved(Local(2026, 1, 22, 9), Workflow.Done);
            jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9)).AssignedTo(People.Two)
                .Moved(Local(2026, 1, 21, 9), Workflow.Development);
        }
    }

    [Test]
    public async Task A_backfill_from_a_later_start_date_clears_an_issue_last_updated_before_it()
    {
        // Such an issue is never fetched again, so nothing else can remove it: not a sync, and not the rule for
        // issues found out of scope, which needs Jira to return the issue (for example one converted to a
        // sub-task before the new start date and not touched since).
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);
        var before = await pipeline.ReadReportAsync(41);
        var storeBefore = await pipeline.ReadStoreContentAsync();

        pipeline.Config["jira"]!["backfillStart"] = "2026-01-15";
        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"], ["report", "--sprint", "42"]);

        await Assert.That(before).Contains("\nDelivered issues: 1\n");
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 0\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 1\n");
        await Assert.That(storeBefore).Contains("FAKE-1");
        await Assert.That(await pipeline.ReadStoreContentAsync()).DoesNotContain("FAKE-1");
    }

    /// <summary>Sprint 41 (5 to 19 January 2026) and sprint 42 (19 January to 2 February), both completed.</summary>
    private static void AddTwoSprints(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        jira.AddSprint(42, "Fake Sprint 42", Local(2026, 1, 19, 10, 30), plannedEnd: Local(2026, 2, 2, 9), completed: Local(2026, 2, 2, 9, 30));
    }
}
