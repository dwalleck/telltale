using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

public class PrivacyTests
{
    // HMAC-SHA256 of "fake-account-0001", computed with openssl, not with the code under test:
    // printf 'fake-account-0001' | openssl dgst -sha256 -hmac '<salt>'
    private const string PersonOneHashedWithSaltOne = "f9fd2e04c99208cd4033758d1e35d2b3a37fc967d7e0ee2a4c500d2a8142941e";
    private const string PersonOneHashedWithSaltTwo = "5dd24bc546424219457808eae071bbf7d8896e0108fe561f0a8f4e224c4e6850";

    [Test]
    public async Task An_assignee_is_stored_as_the_hmac_of_the_account_id_keyed_by_the_salt_from_the_secret_store()
    {
        await using var pipeline = new PipelineHarness();
        AddScenario(pipeline.Jira);

        await pipeline.RunAsync("run", "--sprint", "41");

        var database = await ReadDatabaseAsync(pipeline);
        await Assert.That(database).Contains(PersonOneHashedWithSaltOne);
        await Assert.That(database).DoesNotContain(People.One.AccountId);
    }

    [Test]
    public async Task A_different_salt_in_the_secret_store_gives_a_different_stored_hash()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Secrets[SecretName.HashingSalt] = "synthetic-salt-two";
        AddScenario(pipeline.Jira);

        await pipeline.RunAsync("run", "--sprint", "41");

        var database = await ReadDatabaseAsync(pipeline);
        await Assert.That(database).Contains(PersonOneHashedWithSaltTwo);
        await Assert.That(database).DoesNotContain(PersonOneHashedWithSaltOne);
    }

    [Test]
    public async Task White_space_around_the_salt_does_not_change_the_hash()
    {
        // A salt pasted with a trailing newline must hash people the same way as the clean one.
        await using var pipeline = new PipelineHarness();
        pipeline.Secrets[SecretName.HashingSalt] = "  synthetic-salt-one\r\n";
        AddScenario(pipeline.Jira);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await ReadDatabaseAsync(pipeline)).Contains(PersonOneHashedWithSaltOne);
    }

    [Test]
    public async Task No_account_id_name_email_or_summary_reaches_the_database_the_report_or_the_console()
    {
        await using var pipeline = new PipelineHarness();
        AddScenario(pipeline.Jira);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        var files = await pipeline.ReadAllFilesAsync();

        // The scan is only worth something if it reads the files the data is in: the issue key is
        // stored and the sprint name is reported, so both must be found.
        await Assert.That(files.Keys).Contains("store.db");
        await Assert.That(files["store.db"]).Contains("FAKE-1");
        await Assert.That(files.Keys).Contains(Path.Combine("reports", "sprint-41.md"));
        await Assert.That(files[Path.Combine("reports", "sprint-41.md")]).Contains("Fake Sprint 41");

        var forbidden = pipeline.Jira.ForbiddenText;
        await Assert.That(forbidden).Contains(People.One.AccountId);
        await Assert.That(forbidden).Contains(People.Two.DisplayName);
        await Assert.That(forbidden).Contains(People.One.Email);
        await Assert.That(forbidden).Contains(pipeline.Jira.Issues[0].Summary);
        await Assert.That(forbidden).Contains(pipeline.Jira.Issues[0].Description);

        // Free text and people the fake serves beyond the assignee: the changelog authors, the sprint goal,
        // the project name and the resolution and status descriptions.
        await Assert.That(forbidden).Contains(People.Three.DisplayName);
        await Assert.That(forbidden).Contains(People.BoardAdmin.AccountId);
        await Assert.That(forbidden).Contains("Synthetic goal of sprint 41 that must never be stored");
        await Assert.That(forbidden).Contains(FakeJira.ProjectName);
        await Assert.That(forbidden).Contains(FakeJira.ResolutionDescription);
        await Assert.That(forbidden).Contains(FakeJira.StatusDescription);

        var written = files.Select(file => (Where: file.Key, Text: file.Value))
            .Append((Where: "standard output", Text: pipeline.Out.ToString()))
            .Append((Where: "standard error", Text: pipeline.Error.ToString()));
        var leaks = (from place in written
                     from text in forbidden
                     where place.Text.Contains(text, StringComparison.OrdinalIgnoreCase)
                     select $"'{text}' in {place.Where}").ToList();
        await Assert.That(leaks).IsEmpty();
    }

    [Test]
    public async Task No_secret_reaches_any_file_or_the_console()
    {
        await using var pipeline = new PipelineHarness();
        AddScenario(pipeline.Jira);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        var files = await pipeline.ReadAllFilesAsync();
        await Assert.That(files.Keys).Contains("telltale.json");

        string[] secrets = [PipelineHarness.JiraEmail, PipelineHarness.JiraApiToken, PipelineHarness.HashingSalt];
        var written = files.Select(file => (Where: file.Key, Text: file.Value))
            .Append((Where: "standard output", Text: pipeline.Out.ToString()))
            .Append((Where: "standard error", Text: pipeline.Error.ToString()));
        var leaks = (from place in written
                     from secret in secrets
                     where place.Text.Contains(secret, StringComparison.OrdinalIgnoreCase)
                     select $"a secret in {place.Where}").ToList();
        await Assert.That(leaks).IsEmpty();
    }

    /// <summary>
    /// Two assigned issues delivered in sprint 41 and one unassigned issue still in development. FAKE-1 is
    /// resolved and was moved by someone who is not its assignee; the other moves are the board admin's.
    /// </summary>
    private static void AddScenario(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).AssignedTo(People.One)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development, by: People.Three)
            .Moved(Local(2026, 1, 9, 15), Workflow.Done, by: People.Three)
            .Resolved("300001");
        jira.AddIssue("FAKE-2", Local(2026, 1, 6, 9)).AssignedTo(People.Two)
            .Moved(Local(2026, 1, 8, 9), Workflow.Development)
            .Moved(Local(2026, 1, 14, 11), Workflow.Done);
        jira.AddIssue("FAKE-3", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 12, 9), Workflow.Development);
    }

    /// <summary>The database and any journal files beside it, as text.</summary>
    private static async Task<string> ReadDatabaseAsync(PipelineHarness pipeline)
    {
        var files = await pipeline.ReadAllFilesAsync();
        return string.Concat(files.Where(file => file.Key.StartsWith("store.db", StringComparison.Ordinal)).Select(file => file.Value));
    }
}
