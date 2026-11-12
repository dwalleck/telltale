using Telltale.Cli;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The Credential Manager store over a stand-in for the two Windows calls. The calls themselves
/// (<see cref="WindowsCredentialVault"/>) run only on Windows and are checked by hand there.
/// </summary>
public class CredentialManagerSecretStoreTests
{
    private const string TokenEntry = "Telltale/JiraApiToken";

    // "synthetic-api-token-0001" (the harness's token) as UTF-16 little-endian, written out by hand.
    private static readonly byte[] TokenBytes =
    [
        0x73, 0x00, 0x79, 0x00, 0x6E, 0x00, 0x74, 0x00, 0x68, 0x00, 0x65, 0x00, 0x74, 0x00, 0x69, 0x00, 0x63, 0x00, 0x2D, 0x00,
        0x61, 0x00, 0x70, 0x00, 0x69, 0x00, 0x2D, 0x00, 0x74, 0x00, 0x6F, 0x00, 0x6B, 0x00, 0x65, 0x00, 0x6E, 0x00, 0x2D, 0x00,
        0x30, 0x00, 0x30, 0x00, 0x30, 0x00, 0x31, 0x00,
    ];

    // Base64 of "tool.owner@example.invalid:synthetic-api-token-0001", computed with the base64 command.
    private const string ExpectedAuthorization = "Basic dG9vbC5vd25lckBleGFtcGxlLmludmFsaWQ6c3ludGhldGljLWFwaS10b2tlbi0wMDAx";

    [Test]
    public async Task Each_secret_is_stored_under_its_own_entry_as_utf_16_text()
    {
        await using var pipeline = new PipelineHarness();
        var vault = UseVault(pipeline);
        pipeline.Console.TypeLine(PipelineHarness.JiraApiToken);

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(vault.Entries.Keys).IsEquivalentTo(new[] { TokenEntry });
        await Assert.That(vault.Entries[TokenEntry]).IsEquivalentTo(TokenBytes);
    }

    [Test]
    public async Task Secrets_stored_with_secrets_set_are_the_ones_a_run_uses()
    {
        await using var pipeline = new PipelineHarness();
        var vault = UseVault(pipeline);
        AddOneSprint(pipeline.Jira);

        pipeline.Console.TypeLine(PipelineHarness.JiraEmail);
        await pipeline.RunAsync("secrets", "set", "jira-email");
        pipeline.Console.TypeLine(PipelineHarness.JiraApiToken);
        await pipeline.RunAsync("secrets", "set", "jira-api-token");
        await pipeline.RunAsync("secrets", "set", "hashing-salt", "--generate");
        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(vault.Entries.Keys).IsEquivalentTo(new[] { "Telltale/JiraEmail", TokenEntry, "Telltale/HashingSalt" });
        var sent = pipeline.Jira.Handler.Requests.Select(request => request.Headers.Authorization?.ToString() ?? "(none)").Distinct().ToList();
        await Assert.That(sent).IsEquivalentTo(new[] { ExpectedAuthorization });
    }

    [Test]
    public async Task A_secret_with_no_entry_is_reported_as_not_set()
    {
        await using var pipeline = new PipelineHarness();
        var vault = UseVault(pipeline, withAllSecrets: true);
        vault.Entries.Remove(TokenEntry);
        AddOneSprint(pipeline.Jira);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "The secret 'Jira API token' is not set in the secret store. Run 'telltale secrets set jira-api-token'.\n");
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task A_value_another_tool_stored_with_a_terminating_nul_is_read_without_it(int nulCount)
    {
        await using var pipeline = new PipelineHarness();
        var vault = UseVault(pipeline, withAllSecrets: true);
        vault.Entries[TokenEntry] = [.. TokenBytes, .. new byte[2 * nulCount]];
        AddOneSprint(pipeline.Jira);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        var sent = pipeline.Jira.Handler.Requests.Select(request => request.Headers.Authorization?.ToString() ?? "(none)").Distinct().ToList();
        await Assert.That(sent).IsEquivalentTo(new[] { ExpectedAuthorization });
    }

    [Test]
    [Arguments("an odd number of bytes", new byte[] { 0x61, 0x00, 0x62 })]
    [Arguments("a NUL in the middle", new byte[] { 0x61, 0x00, 0x00, 0x00, 0x62, 0x00 })]
    [Arguments("a line break in the middle", new byte[] { 0x61, 0x00, 0x0A, 0x00, 0x62, 0x00 })]
    public async Task An_entry_that_is_not_one_line_of_utf_16_text_stops_the_run_and_says_to_set_it_again(string what, byte[] stored)
    {
        await using var pipeline = new PipelineHarness();
        var vault = UseVault(pipeline, withAllSecrets: true);
        vault.Entries[TokenEntry] = stored;
        AddOneSprint(pipeline.Jira);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1).Because(what);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "The secret 'Jira API token' in Windows Credential Manager is not text this tool stored, so it cannot be used. Set it again. Run 'telltale secrets set jira-api-token'.\n");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    public async Task A_machine_whose_policy_forbids_storing_credentials_gets_a_message_naming_the_policy()
    {
        // Windows answers 1312 (no such logon session) when that policy is on.
        await using var pipeline = new PipelineHarness();
        var vault = UseVault(pipeline);
        vault.WriteError = 1312;
        pipeline.Console.TypeLine("synthetic-api-token-0002");

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).EndsWith(
            "\nWindows refused to store the secret 'Jira API token' (error 1312). This usually means the policy \"Network access: Do not allow storage of passwords and credentials for network authentication\" is on for this machine, and then Credential Manager cannot hold the tool's secrets. Nothing was stored.\n");
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(pipeline.Error.ToString()).DoesNotContain("synthetic-api-token-0002");
    }

    [Test]
    public async Task Any_other_failure_to_store_names_the_secret_and_the_windows_error_and_reports_nothing_stored()
    {
        await using var pipeline = new PipelineHarness();
        var vault = UseVault(pipeline);
        vault.WriteError = 5;
        pipeline.Console.TypeLine("synthetic-api-token-0002");

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("Windows Credential Manager could not store the secret 'Jira API token'").And.Contains("(error 5)");
        await Assert.That(pipeline.Error.ToString()).DoesNotContain("policy");
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(pipeline.Error.ToString()).DoesNotContain("synthetic-api-token-0002");
    }

    [Test]
    public async Task A_failure_to_read_stops_the_run_and_names_the_secret_and_the_windows_error()
    {
        await using var pipeline = new PipelineHarness();
        var vault = UseVault(pipeline, withAllSecrets: true);
        vault.ReadError = 5;
        AddOneSprint(pipeline.Jira);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("Windows Credential Manager could not read the secret 'Jira email'").And.Contains("(error 5)");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    public async Task A_value_longer_than_windows_can_hold_is_refused_and_one_of_exactly_that_length_is_stored()
    {
        // Windows holds 2560 bytes per credential: 1280 UTF-16 code units.
        await using var pipeline = new PipelineHarness();
        var vault = UseVault(pipeline);

        pipeline.Console.TypeLine(new string('a', 1281));
        var tooLong = await pipeline.RunAsync("secrets", "set", "jira-api-token");
        var entriesAfterTooLong = vault.Entries.Count;
        pipeline.Console.TypeLine(new string('a', 1280));
        var longest = await pipeline.RunAsync("secrets", "set", "jira-api-token");

        await Assert.That(tooLong).IsEqualTo(1);
        await Assert.That(entriesAfterTooLong).IsEqualTo(0);
        await Assert.That(pipeline.Error.ToString()).Contains(
            "The secret 'Jira API token' is longer than the 1280 characters Windows Credential Manager can hold. Nothing was stored.");
        await Assert.That(longest).IsEqualTo(0);
        await Assert.That(vault.Entries[TokenEntry].Length).IsEqualTo(2560);
    }

    private const string SaltEntry = "Telltale/HashingSalt";

    // Three bytes: not UTF-16 text, so the store cannot read it.
    private static readonly byte[] NotText = [0x61, 0x00, 0x62];

    [Test]
    public async Task A_stored_salt_that_cannot_be_read_does_not_stop_a_new_one_being_set()
    {
        // The message about an unreadable secret says to set it again, so setting it must work.
        await using var pipeline = new PipelineHarness();
        var vault = UseVault(pipeline);
        vault.Entries[SaltEntry] = [.. NotText];
        pipeline.Console.TypeLine("synthetic-salt-three");

        var exit = await pipeline.RunAsync("secrets", "set", "hashing-salt");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(System.Text.Encoding.Unicode.GetString(vault.Entries[SaltEntry])).IsEqualTo("synthetic-salt-three");
    }

    private const string UnreadableSaltOverHashedPeople =
        "The stored hashing salt is not text this tool stored, so it cannot be used, and the database already holds people hashed with a salt. "
        + "Only that salt keeps each of them one person: to restore it, run 'telltale secrets set hashing-salt --replace' and type the original salt. "
        + "A new or generated salt stored with --replace makes every person count twice until a backfill.\n";

    [Test]
    public async Task A_typed_salt_replaces_a_stored_one_that_cannot_be_read_over_hashed_people_only_with_the_replace_option()
    {
        await using var pipeline = new PipelineHarness();
        var vault = await RunOnceWithAnAssigneeThenMakeTheSaltUnreadableAsync(pipeline);

        // Typed once: the refused command stops before it asks, so the line is still there for the second.
        pipeline.Console.TypeLine("synthetic-salt-three");
        var refused = await pipeline.RunAsync("secrets", "set", "hashing-salt");
        var errorWhenRefused = pipeline.Error.ToString().ReplaceLineEndings("\n");
        var entryWhenRefused = vault.Entries[SaltEntry];
        var replaced = await pipeline.RunAsync("secrets", "set", "hashing-salt", "--replace");

        await Assert.That(refused).IsEqualTo(1);
        await Assert.That(errorWhenRefused).IsEqualTo(UnreadableSaltOverHashedPeople);
        await Assert.That(entryWhenRefused).IsEquivalentTo(NotText);
        await Assert.That(replaced).IsEqualTo(0);
        await Assert.That(System.Text.Encoding.Unicode.GetString(vault.Entries[SaltEntry])).IsEqualTo("synthetic-salt-three");
    }

    [Test]
    public async Task A_generated_salt_replaces_a_stored_one_that_cannot_be_read_over_hashed_people_only_with_the_replace_option()
    {
        await using var pipeline = new PipelineHarness();
        var vault = await RunOnceWithAnAssigneeThenMakeTheSaltUnreadableAsync(pipeline);

        var refused = await pipeline.RunAsync("secrets", "set", "hashing-salt", "--generate");
        var errorWhenRefused = pipeline.Error.ToString().ReplaceLineEndings("\n");
        var entryWhenRefused = vault.Entries[SaltEntry];
        var replaced = await pipeline.RunAsync("secrets", "set", "hashing-salt", "--generate", "--replace");

        await Assert.That(refused).IsEqualTo(1);
        await Assert.That(errorWhenRefused).IsEqualTo(UnreadableSaltOverHashedPeople);
        await Assert.That(entryWhenRefused).IsEquivalentTo(NotText);
        await Assert.That(replaced).IsEqualTo(0);

        // A generated salt is 256 random bits, stored as base64 text.
        await Assert.That(Convert.FromBase64String(System.Text.Encoding.Unicode.GetString(vault.Entries[SaltEntry])).Length).IsEqualTo(32);
    }

    /// <summary>A store that holds one hashed person, and a salt entry Credential Manager can no longer give as text.</summary>
    private static async Task<FakeCredentialVault> RunOnceWithAnAssigneeThenMakeTheSaltUnreadableAsync(PipelineHarness pipeline)
    {
        var vault = UseVault(pipeline, withAllSecrets: true);
        AddOneSprint(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).AssignedTo(People.One)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        vault.Entries[SaltEntry] = [.. NotText];
        return vault;
    }

    [Test]
    public async Task A_failure_to_read_the_stored_salt_is_reported_as_that_and_nothing_is_stored()
    {
        // A read that failed says nothing about what is stored: the salt may be there and good.
        await using var pipeline = new PipelineHarness();
        var vault = UseVault(pipeline, withAllSecrets: true);
        vault.ReadError = 5;
        pipeline.Console.TypeLine("synthetic-salt-three");

        var exit = await pipeline.RunAsync("secrets", "set", "hashing-salt");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("Windows Credential Manager could not read the secret 'hashing salt'").And.Contains("(error 5)");
        await Assert.That(System.Text.Encoding.Unicode.GetString(vault.Entries[SaltEntry])).IsEqualTo(PipelineHarness.HashingSalt);
    }

    /// <summary>Gives the commands the Credential Manager store over a stand-in vault, empty or holding the harness's three secrets.</summary>
    private static FakeCredentialVault UseVault(PipelineHarness pipeline, bool withAllSecrets = false)
    {
        var vault = new FakeCredentialVault();
        if (withAllSecrets)
        {
            vault.Entries["Telltale/JiraEmail"] = System.Text.Encoding.Unicode.GetBytes(PipelineHarness.JiraEmail);
            vault.Entries[TokenEntry] = [.. TokenBytes];
            vault.Entries["Telltale/HashingSalt"] = System.Text.Encoding.Unicode.GetBytes(PipelineHarness.HashingSalt);
        }

        pipeline.SecretStore = new CredentialManagerSecretStore(vault);
        return vault;
    }

    private static void AddOneSprint(FakeJira jira) =>
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
}
