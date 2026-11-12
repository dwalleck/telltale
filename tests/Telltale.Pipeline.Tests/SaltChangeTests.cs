using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// Every person hash in the store is made with one salt. The minimum group size counts distinct hashes
/// (ADR-0003), so a person hashed under two salts would count as two people.
/// </summary>
public class SaltChangeTests
{
    // printf 'fake-account-0001' | openssl dgst -sha256 -hmac '<salt>'
    private const string PersonOneHashedWithSaltOne = "f9fd2e04c99208cd4033758d1e35d2b3a37fc967d7e0ee2a4c500d2a8142941e";
    private const string PersonOneHashedWithSaltTwo = "5dd24bc546424219457808eae071bbf7d8896e0108fe561f0a8f4e224c4e6850";
    private const string PersonTwoHashedWithSaltOne = "6718063a6f22307ddc5e634af96bbf55fa967e99c80c9bf389c6d1883e599bed";

    private const string Refusal =
        "The hashing salt in the secret store is not the one the people in the database were hashed with, so one person would count as two. "
        + "Restore the original salt, or run backfill to hash everyone again with this one.\n";

    [Test]
    [Arguments("sync")]
    [Arguments("run")]
    [Arguments("derive")]
    [Arguments("report")]
    public async Task A_command_under_another_salt_than_the_stored_people_were_hashed_with_stops_and_asks_for_a_backfill(string command)
    {
        await using var pipeline = new PipelineHarness();
        AddScenario(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var storeBefore = await pipeline.ReadStoreContentAsync();
        var requestsBefore = pipeline.Jira.Handler.Requests.Count;
        pipeline.Secrets[SecretName.HashingSalt] = "synthetic-salt-two";

        var exit = await pipeline.RunAsync(command);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(Refusal);
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(requestsBefore);
        await Assert.That(await pipeline.ReadStoreContentAsync()).IsEqualTo(storeBefore);
        await Assert.That(Directory.Exists(pipeline.ReportDirectory)).IsFalse();
    }

    [Test]
    public async Task A_backfill_under_a_new_salt_hashes_every_stored_person_again_and_the_other_commands_work_after_it()
    {
        await using var pipeline = new PipelineHarness();
        AddScenario(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"]);
        var storeBefore = await pipeline.ReadStoreContentAsync();
        pipeline.Secrets[SecretName.HashingSalt] = "synthetic-salt-two";

        var exit = await pipeline.RunAsync("backfill");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(storeBefore).Contains(PersonOneHashedWithSaltOne).And.Contains(PersonTwoHashedWithSaltOne);
        var store = await pipeline.ReadStoreContentAsync();
        await Assert.That(store).Contains(PersonOneHashedWithSaltTwo);
        await Assert.That(store).DoesNotContain(PersonOneHashedWithSaltOne).And.DoesNotContain(PersonTwoHashedWithSaltOne);
        await pipeline.RunAllAsync(["sync"], ["derive"], ["report", "--sprint", "41"]);
    }

    [Test]
    public async Task A_backfill_under_a_new_salt_stops_and_stores_nothing_when_a_stored_person_cannot_be_hashed_again()
    {
        await using var pipeline = new PipelineHarness();
        AddScenario(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"]);
        var storeBefore = await pipeline.ReadStoreContentAsync();

        // Jira no longer returns FAKE-2 (last updated on 9 January), so the tool never sees its assignee's account again.
        pipeline.Config["jira"]!["backfillStart"] = "2026-01-12";
        pipeline.Secrets[SecretName.HashingSalt] = "synthetic-salt-two";

        var exit = await pipeline.RunAsync("backfill");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "1 issue in the database is no longer in Jira's answer, so its assignee cannot be hashed again with the new salt. Nothing was stored. "
            + "Restore the original salt, or delete the database file and run backfill.\n");
        await Assert.That(await pipeline.ReadStoreContentAsync()).IsEqualTo(storeBefore);

        // The store still says which salt its people were hashed with.
        pipeline.Config["jira"]!["backfillStart"] = "2025-01-01";
        pipeline.Error.GetStringBuilder().Clear();
        await Assert.That(await pipeline.RunAsync("sync")).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(Refusal);
    }

    [Test]
    public async Task A_store_that_holds_people_but_does_not_say_which_salt_hashed_them_is_treated_as_hashed_with_another_salt()
    {
        // A store filled by a version of the tool from before the salt was recorded.
        await using var pipeline = new PipelineHarness();
        AddScenario(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"]);
        await pipeline.ExecuteAsync("DELETE FROM person_hashing;");

        var exit = await pipeline.RunAsync("sync");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(Refusal);
        await pipeline.RunAllAsync(["backfill"], ["sync"]);
    }

    [Test]
    public async Task The_salt_itself_is_not_in_the_store()
    {
        await using var pipeline = new PipelineHarness();
        AddScenario(pipeline.Jira);

        await pipeline.RunAllAsync(["backfill"]);

        // What is stored is a keyed digest of a fixed text: printf 'telltale salt fingerprint' | openssl dgst -sha256 -hmac 'synthetic-salt-one'
        var store = await pipeline.ReadStoreContentAsync();
        await Assert.That(store).Contains("ddd9d5eb5e8d1f52028ee5b9972e6e2a70fa201ab9e66a84198397e93f39bba5");
        await Assert.That(store).DoesNotContain(PipelineHarness.HashingSalt);
    }

    /// <summary>Sprint 41 (5 to 19 January 2026, completed), with one issue of person one and one of person two.</summary>
    private static void AddScenario(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).AssignedTo(People.One)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 14, 9), Workflow.Done);
        jira.AddIssue("FAKE-2", Local(2026, 1, 6, 9)).AssignedTo(People.Two)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);
    }
}
