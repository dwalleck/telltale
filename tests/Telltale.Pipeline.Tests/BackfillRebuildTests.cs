using System.Net;
using Telltale.Core.Identity;
using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// <c>backfill</c> rebuilds the raw tables: afterwards they hold what Jira returned to that backfill and
/// nothing else (owner decision D4). <c>sync</c> only adds and updates.
/// </summary>
public class BackfillRebuildTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task A_lost_or_corrupt_database_costs_one_backfill(bool corrupt)
    {
        // Story 47: the database is disposable. The store was built up over a backfill and a later sync.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprints41And42();
        pipeline.Jira.AddTeam(41, 42);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9)).AssignedTo(People.One).Estimated(3).InSprints(42)
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        var later = pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9)).AssignedTo(People.Two).InSprints(42)
            .Moved(Local(2026, 1, 21, 9), Workflow.Development);
        await pipeline.RunAllAsync(["backfill"]);
        later.Moved(Local(2026, 1, 30, 9), Workflow.Done);
        await pipeline.RunAllAsync(["sync"], ["derive"], ["report", "--sprint", "42"]);
        var storeBefore = await pipeline.ReadStoreContentAsync(withoutAsOf: true);
        var reportBefore = await pipeline.ReadReportBytesAsync(42, withoutAsOf: true);

        if (corrupt)
        {
            // What is left of a file after a bad copy. Every command says the file cannot be used and changes nothing.
            await File.WriteAllTextAsync(pipeline.DatabasePath, "Synthetic bytes that are not a database, long enough to be read as a header by SQLite.");
            var refused = await pipeline.RunAsync("backfill");
            await Assert.That(refused).IsEqualTo(1);
            await Assert.That(pipeline.Error.ToString()).StartsWith($"The database {pipeline.DatabasePath} could not be used: ");
        }

        File.Delete(pipeline.DatabasePath);
        File.Delete(Path.Combine(pipeline.ReportDirectory, "sprint-42.md"));
        pipeline.Clock.Advance(TimeSpan.FromDays(1));
        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "42"]);

        await Assert.That(storeBefore).Contains(pipeline.Jira.StoredRowOf("FAKE-2"));
        await Assert.That(await pipeline.ReadStoreContentAsync(withoutAsOf: true)).IsEqualTo(storeBefore);
        await Assert.That(await pipeline.ReadReportBytesAsync(42, withoutAsOf: true)).IsEqualTo(reportBefore);
    }

    [Test]
    public async Task An_issue_deleted_in_jira_is_still_counted_after_a_sync_and_no_longer_after_a_backfill()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprints41And42();
        pipeline.Jira.AddTeam(41, 42);
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

        await Assert.That(beforeTheDeletion).Contains("\nDelivered issues: 5\n");
        await Assert.That(afterSync).Contains("\nDelivered issues: 5\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 4\n");

        // Not only uncounted: no row of the issue is left, raw or derived.
        await Assert.That(storeAfterSync).Contains(pipeline.Jira.StoredRowOf("FAKE-2"));
        await Assert.That(await pipeline.ReadStoreContentAsync()).DoesNotContain(pipeline.Jira.StoredRowOf("FAKE-2"));
    }

    [Test]
    public async Task A_sprint_removed_from_the_board_in_jira_stays_after_a_sync_and_is_gone_from_the_store_after_a_backfill()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        var removed = pipeline.Jira.AddSprint42();
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
        await Assert.That(store).Contains(changed.Jira.StoredRowOf("FAKE-1"));
        await Assert.That(store).IsEqualTo(await fresh.ReadStoreContentAsync());

        static void BuildBoard(FakeJira jira)
        {
            jira.AddSprints41And42();
            jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).Estimated(3).InSprints(41)
                .Reestimated(Local(2026, 1, 7, 9), 8)
                .SprintsChanged(Local(2026, 1, 20, 9), 42)
                .Moved(Local(2026, 1, 21, 9), Workflow.Development)
                .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        }
    }

    [Test]
    public async Task A_status_that_jira_no_longer_lists_keeps_its_name_in_the_report_after_a_backfill()
    {
        // Statuses are reference data: stored history still names a status by ID after Jira stops listing it,
        // and the report needs its name. A rebuild does not clear them.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprints41And42();
        pipeline.Jira.ServeEachIssueOncePer(People.Team);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9)).InSprints(41)
            .Moved(Local(2026, 1, 6, 9), Workflow.Review)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);
        await pipeline.RunAllAsync(["backfill"]);

        pipeline.Jira.DeleteStatus(Workflow.Review);
        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\n| Fake Review | 5 | 2.0 | 10.0 |\n");
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
        await Assert.That(storeBefore).Contains(interrupted.Jira.StoredRowOf("FAKE-2"));
        await Assert.That(await interrupted.ReadReportAsync(42)).Contains("\nDelivered issues: 4\n");
        await Assert.That(storeAfterFailure).IsEqualTo(storeBefore);
        await Assert.That(reportAfterFailure).IsEqualTo(reportBefore);
        var store = await interrupted.ReadStoreContentAsync();
        await Assert.That(store).DoesNotContain(interrupted.Jira.StoredRowOf("FAKE-2"));
        await Assert.That(store).IsEqualTo(await uninterrupted.ReadStoreContentAsync());

        static void BuildBoard(FakeJira jira)
        {
            jira.AddSprints41And42();
            jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9)).AssignedTo(People.One)
                .Moved(Local(2026, 1, 21, 9), Workflow.Development);
            jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9)).AssignedTo(People.Two)
                .Moved(Local(2026, 1, 21, 9), Workflow.Development)
                .Moved(Local(2026, 1, 23, 9), Workflow.Done);

            // A team of five, so the report compared shows its figures.
            jira.AddTeam(41, 42);
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
        pipeline.Jira.AddSprints41And42();
        var kept = pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9)).AssignedTo(People.One).Estimated(3).InSprints(42)
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        var deleted = pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9)).AssignedTo(People.Two)
            .Moved(Local(2026, 1, 21, 9), Workflow.Development);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var before = RowsOf(kept, await pipeline.ReadStoreContentAsync());
        var bytesBefore = await pipeline.ReadDatabaseBytesAsync();

        pipeline.Jira.Delete(deleted);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        // Every row that names the issue, raw and derived: its issue row with the hashed assignee, its changes,
        // its intervals and its cycle.
        var store = await pipeline.ReadStoreContentAsync();
        await Assert.That(before).Contains(pipeline.Jira.StoredRowOf("FAKE-1"));
        await Assert.That(before.Split('\n')).Count().IsGreaterThanOrEqualTo(4);
        await Assert.That(before).Matches($"(?m)^{kept.Id} \\| .* \\| [0-9a-f]{{64}} \\| .*$");
        await Assert.That(RowsOf(kept, store)).IsEqualTo(before);
        await Assert.That(store).DoesNotContain(pipeline.Jira.StoredRowOf("FAKE-2"));

        // Gone from the file too, not only from the tables: a deleted row must not stay readable in a free page.
        var hashOfTheDeletedIssuesAssignee = IdentityHash.Of(People.Two.AccountId, PipelineHarness.HashingSalt);
        await Assert.That(bytesBefore).Contains(hashOfTheDeletedIssuesAssignee);
        await Assert.That(await pipeline.ReadDatabaseBytesAsync()).DoesNotContain(hashOfTheDeletedIssuesAssignee);

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
            jira.AddSprints41And42();
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
        var bytesUnderTheOldSalt = await resalted.ReadDatabaseBytesAsync();
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
        await Assert.That(underTheOldSalt).Contains(resalted.Jira.StoredRowOf("FAKE-2"));
        await Assert.That(store).IsNotEqualTo(await oldSalt.ReadStoreContentAsync());
        await Assert.That(store).IsEqualTo(await fresh.ReadStoreContentAsync());

        // The file holds no hash under the old salt either, nor the old salt's fingerprint: the rows a
        // backfill deletes must not stay readable in the file's free pages.
        string[] underOldSalt = [.. People.Team.Take(2).Select(person => IdentityHash.Of(person.AccountId, PipelineHarness.HashingSalt)), IdentityHash.SaltFingerprint(PipelineHarness.HashingSalt)];
        var bytes = await resalted.ReadDatabaseBytesAsync();
        await Assert.That(underOldSalt.Where(hash => !bytesUnderTheOldSalt.Contains(hash, StringComparison.Ordinal))).IsEmpty();
        await Assert.That(underOldSalt.Where(hash => bytes.Contains(hash, StringComparison.Ordinal))).IsEmpty();

        static void BuildBoard(FakeJira jira)
        {
            jira.AddSprints41And42();
            jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9)).AssignedTo(People.One)
                .Moved(Local(2026, 1, 21, 9), Workflow.Development)
                .Moved(Local(2026, 1, 22, 9), Workflow.Done);
            jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9)).AssignedTo(People.Two)
                .Moved(Local(2026, 1, 21, 9), Workflow.Development);
        }
    }

    [Test]
    public async Task Run_without_a_sprint_when_the_last_completed_one_started_before_the_start_date_updates_the_store_says_why_and_succeeds()
    {
        // B11 of the final review, as owner decision D28 has it for "no sprint has completed": the owner asked
        // for no particular sprint, the store was brought up to date, and why there is no report is said, not failed.
        // Sprint 41 started on 5 January and the store is filled from the 15th.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Jira.AddSprint42(plannedEnd: Local(2026, 2, 16, 9), stillRunning: true);
        pipeline.Jira.AddTeam(41, 42);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        pipeline.Config["jira"]!["backfillStart"] = "2026-01-15";

        var run = await pipeline.RunAsync("run");
        var outputOfRun = pipeline.Out.ToString().ReplaceLineEndings("\n");
        var report = await pipeline.RunAsync("report");

        const string Why =
            "Sprint 41 started before 2026-01-15, the jira.backfillStart the store was last filled from, so issues delivered in it can be missing and its figures would be too low. "
            + "No report was written. To report on it, set jira.backfillStart to a date before the sprint started and run backfill.\n";
        await Assert.That(run).IsEqualTo(0);
        await Assert.That(outputOfRun).IsEqualTo("The store is up to date. " + Why);
        await Assert.That(await pipeline.QueryAsync<long>("SELECT delivered FROM issue_cycle WHERE issue_id = @id;", new { id = pipeline.Jira.IdOf("FAKE-1") })).IsEquivalentTo(new[] { 1L });
        await Assert.That(Directory.Exists(pipeline.ReportDirectory)).IsFalse();

        // A command whose only job is the report still fails.
        await Assert.That(report).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(Why);
    }

    [Test]
    public async Task A_backfill_from_a_later_start_date_clears_an_issue_last_updated_before_it()
    {
        // Such an issue is never fetched again, so nothing else can remove it: not a sync, and not the rule for
        // issues found out of scope, which needs Jira to return the issue (for example one converted to a
        // sub-task before the new start date and not touched since).
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprints41And42();
        pipeline.Jira.ServeEachIssueOncePer(People.Team);
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
        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "42"]);
        var report = await pipeline.RunAsync("report", "--sprint", "41");
        var run = await pipeline.RunAsync("run", "--sprint", "41");

        // Sprint 41 began before the new start date, so its deliveries can be missing (here its only one is):
        // a report on it is refused, and the one written before stays as it was.
        const string Refusal =
            "Sprint 41 started before 2026-01-15, the jira.backfillStart the store was last filled from, so issues delivered in it can be missing and its figures would be too low. "
            + "No report was written. To report on it, set jira.backfillStart to a date before the sprint started and run backfill.\n";
        await Assert.That(report).IsEqualTo(1);
        await Assert.That(run).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(Refusal + Refusal);
        await Assert.That(before).Contains("\nDelivered issues: 5\n");
        await Assert.That(await pipeline.ReadReportAsync(41)).IsEqualTo(before);
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 5\n");
        await Assert.That(storeBefore).Contains(pipeline.Jira.StoredRowOf("FAKE-1"));
        await Assert.That(await pipeline.ReadStoreContentAsync()).DoesNotContain(pipeline.Jira.StoredRowOf("FAKE-1"));
    }

    [Test]
    public async Task A_backfill_says_how_many_issues_sprints_and_changes_the_store_held_before_and_holds_now()
    {
        // Counts only. They are the sign of a backfill that replaced much with little.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprints41And42();
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        var deleted = pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development);
        await pipeline.RunAllAsync(["backfill"]);

        pipeline.Jira.Delete(deleted);
        await pipeline.RunAllAsync(["backfill"]);

        await Assert.That(pipeline.Out.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "The store now holds what Jira returned. Issues: 0 before, 2 now. Sprints: 0 before, 2 now. Changes: 0 before, 3 now.\n"
            + "The store now holds what Jira returned. Issues: 2 before, 1 now. Sprints: 2 before, 2 now. Changes: 3 before, 2 now.\n");
    }

    [Test]
    [Arguments("issues", "Jira returned no issues to this backfill, but the store holds 2.")]
    [Arguments("sprints", "Jira returned no sprints to this backfill, but the store holds 2.")]
    public async Task A_backfill_that_would_replace_stored_issues_or_sprints_with_none_is_refused_unless_it_is_allowed(string missing, string finding)
    {
        // What a wrong projectKeys or boardId, or an account that lost its access, looks like: Jira answers
        // normally and returns nothing.
        await using var pipeline = new PipelineHarness();
        var sprints = new[]
        {
            pipeline.Jira.AddSprint41(),
            pipeline.Jira.AddSprint42(),
        };
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9)).Moved(Local(2026, 1, 21, 9), Workflow.Development);
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9)).Moved(Local(2026, 1, 22, 9), Workflow.Development);
        await pipeline.RunAllAsync(["backfill"]);
        var storeBefore = await pipeline.ReadStoreContentAsync();
        var gone = missing == "issues" ? pipeline.Jira.StoredRowOf("FAKE-1") : "Fake Sprint";

        if (missing == "issues")
        {
            pipeline.Jira.Delete(pipeline.Jira.Issues[1]);
            pipeline.Jira.Delete(pipeline.Jira.Issues[0]);
        }
        else
        {
            pipeline.Jira.RemoveSprint(sprints[0]);
            pipeline.Jira.RemoveSprint(sprints[1]);
        }

        var refused = await pipeline.RunAsync("backfill");
        var storeAfterRefusal = await pipeline.ReadStoreContentAsync();
        var allowed = await pipeline.RunAsync("backfill", "--allow-empty");

        await Assert.That(refused).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            finding + " A backfill replaces what the store holds, so this one would remove them all. "
            + "Check jira.projectKeys and jira.boardId in the configuration file, and that the Jira account can still see the projects and the board. "
            + "Nothing was stored. If Jira really holds none, run backfill --allow-empty.\n");
        await Assert.That(storeBefore).Contains(gone);
        await Assert.That(storeAfterRefusal).IsEqualTo(storeBefore);
        await Assert.That(allowed).IsEqualTo(0);
        await Assert.That(await pipeline.ReadStoreContentAsync()).DoesNotContain(gone);
    }

    [Test]
    public async Task A_first_backfill_to_which_jira_returns_nothing_is_not_refused()
    {
        // The store holds nothing a backfill could remove, so there is nothing to protect.
        await using var pipeline = new PipelineHarness();

        var exit = await pipeline.RunAsync("backfill");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Out.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "The store now holds what Jira returned. Issues: 0 before, 0 now. Sprints: 0 before, 0 now. Changes: 0 before, 0 now.\n");
    }

    [Test]
    [Arguments(9, "2026-01-05", 0)]
    [Arguments(9, "2026-01-06", 1)]
    [Arguments(5, "2026-01-05", 1)]
    public async Task A_report_is_refused_only_for_a_sprint_that_can_have_started_before_the_start_date_in_the_jira_accounts_time_zone(int startHour, string backfillStart, int expectedExit)
    {
        // Jira reads the start date in the time zone of the account asking, which the tool does not know. The
        // date begins last at twelve hours behind UTC, which is 06:00 in the team's zone in January: a sprint
        // that started at 09:00 on the start date is whole for any account, one that started at 05:00 may not be.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41(start: Local(2026, 1, 5, startHour));
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);
        pipeline.Config["jira"]!["backfillStart"] = backfillStart;
        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        var exit = await pipeline.RunAsync("report", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(expectedExit);
        await Assert.That(File.Exists(Path.Combine(pipeline.ReportDirectory, "sprint-41.md"))).IsEqualTo(expectedExit == 0);
    }

}
