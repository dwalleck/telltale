using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.PrivacyScan;
using static Telltale.Tests.Shared.TeamTime;

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
        await AssertNothingForbiddenWasWrittenAsync(pipeline);
    }

    [Test]
    public async Task No_issue_key_reaches_the_database_the_report_or_the_console()
    {
        // Owner decision D52: nothing reads the key, and beside a person hash it is the easiest way back to a
        // person, since anyone can look up an issue's assignee. The Jira issue ID identifies the issue.
        await using var pipeline = new PipelineHarness();
        AddScenario(pipeline.Jira);

        // An anomaly, so the log has a line about an issue.
        pipeline.Jira.AddIssue("FAKE-4", Local(2026, 1, 8, 9), createdIn: Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        var files = await pipeline.ReadAllFilesAsync();
        await Assert.That(files["store.db"]).Contains(pipeline.Jira.IdOf("FAKE-1"));
        await Assert.That(pipeline.Error.ToString()).Contains($"issue {pipeline.Jira.IdOf("FAKE-4")}");
        var written = files.Select(file => (Where: file.Key, Text: file.Value))
            .Append((Where: "standard output", Text: pipeline.Out.ToString().Replace(pipeline.Directory, "", StringComparison.Ordinal)))
            .Append((Where: "standard error", Text: pipeline.Error.ToString()));
        var leaks = written.Where(place => place.Text.Contains(FakeJira.ProjectKey + "-", StringComparison.OrdinalIgnoreCase)).Select(place => place.Where).ToList();
        await Assert.That(leaks).IsEmpty();
        await Assert.That(await pipeline.QueryAsync<string>("SELECT name FROM pragma_table_info('issue') WHERE name LIKE '%key%';")).IsEmpty();
    }

    [Test]
    public async Task A_store_that_held_issue_keys_holds_none_in_its_tables_or_its_bytes_after_the_next_command_nor_what_was_deleted_before()
    {
        // A store as the version before owner decision D52 left it: the issue table has the key column.
        await using var pipeline = new PipelineHarness();
        AddScenario(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"]);
        await pipeline.ExecuteAsync(
            """
            ALTER TABLE issue ADD COLUMN issue_key TEXT NULL;
            UPDATE issue SET issue_key = 'FAKE-' || substr(issue_id, -1);
            DELETE FROM schema_migration WHERE name = '0007_drop_issue_key.sql';
            """);

        // And a row that version deleted without overwriting it, as it deleted the rows of a converted issue
        // or of people hashed under an old salt: still in the file, in no table.
        await pipeline.ExecuteAsync(
            """
            PRAGMA secure_delete = OFF;
            INSERT INTO status (status_id, name, category) VALUES ('999999', 'Synthetic text of a row deleted before the migration ran', 'new');
            DELETE FROM status WHERE status_id = '999999';
            """);
        var bytesBefore = await pipeline.ReadDatabaseBytesAsync();

        await pipeline.RunAllAsync(["derive"]);

        await Assert.That(bytesBefore).Contains("FAKE-1");
        await Assert.That(bytesBefore).Contains("of a row deleted before the migration ran");
        await Assert.That(await pipeline.ReadDatabaseBytesAsync()).DoesNotContain("of a row deleted before the migration ran");
        await Assert.That(await pipeline.QueryAsync<string>("SELECT name FROM pragma_table_info('issue') WHERE name LIKE '%key%';")).IsEmpty();
        await Assert.That(await pipeline.ReadDatabaseBytesAsync()).DoesNotContain("FAKE-");
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

    /// <summary>The database and any journal files beside it, as text.</summary>
    private static async Task<string> ReadDatabaseAsync(PipelineHarness pipeline)
    {
        var files = await pipeline.ReadAllFilesAsync();
        return string.Concat(files.Where(file => file.Key.StartsWith("store.db", StringComparison.Ordinal)).Select(file => file.Value));
    }
}
