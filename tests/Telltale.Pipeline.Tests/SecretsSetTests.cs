using Telltale.Cli;
using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

public class SecretsSetTests
{
    [Test]
    [Arguments("jira-email", SecretName.JiraEmail, "'Jira email'", "second.owner@example.invalid")]
    [Arguments("jira-api-token", SecretName.JiraApiToken, "'Jira API token'", "synthetic-api-token-0002")]
    [Arguments("hashing-salt", SecretName.HashingSalt, "'hashing salt'", "synthetic-salt-three")]
    public async Task Secrets_set_asks_for_the_named_secret_and_stores_what_is_typed(string argument, SecretName name, string label, string typed)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Secrets.Clear();
        pipeline.Console.TypeLine(typed);

        var exit = await pipeline.RunAsync("secrets", "set", argument);

        // The question is asked on standard error, and nothing typed is shown on either stream.
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo($"Value for the secret {label} (not shown as you type): \n");
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Secrets.Keys).IsEquivalentTo(new[] { name });
        await Assert.That(pipeline.Secrets[name]).IsEqualTo(typed);
        await Assert.That(pipeline.Out.ToString().ReplaceLineEndings("\n")).IsEqualTo($"Stored the secret {label}.\n");
    }

    [Test]
    public async Task White_space_around_a_typed_secret_is_not_stored()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Console.TypeLine("  synthetic-api-token-0002 ");

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken]).IsEqualTo("synthetic-api-token-0002");
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task An_empty_answer_stores_nothing_and_names_the_secret(string typed)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Console.TypeLine(typed);

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).EndsWith("\nNo value was given for the secret 'Jira API token'. Nothing was stored.\n");
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken]).IsEqualTo(PipelineHarness.JiraApiToken);
    }

    [Test]
    public async Task A_stored_email_or_token_is_replaced_without_being_asked_twice()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Console.TypeLine("second.owner@example.invalid");

        var exit = await pipeline.RunAsync("secrets", "set", "jira-email");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Secrets[SecretName.JiraEmail]).IsEqualTo("second.owner@example.invalid");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task A_stored_hashing_salt_is_not_replaced_unless_asked_to(bool generate)
    {
        // A new salt gives every person a new hash, so one person would count as two across the change.
        await using var pipeline = new PipelineHarness();
        pipeline.Console.TypeLine("synthetic-salt-three");
        string[] args = generate ? ["secrets", "set", "hashing-salt", "--generate"] : ["secrets", "set", "hashing-salt"];

        var exit = await pipeline.RunAsync(args);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "A hashing salt is already stored. A different salt gives every person a different hash, so the hashes already in the database would stop matching new ones. To replace it anyway, add --replace.\n");
        await Assert.That(pipeline.Secrets[SecretName.HashingSalt]).IsEqualTo(PipelineHarness.HashingSalt);
        await Assert.That(pipeline.Console.KeysRead).IsEqualTo(0);
    }

    [Test]
    public async Task A_stored_hashing_salt_is_replaced_with_the_replace_option()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Console.TypeLine("synthetic-salt-three");

        var exit = await pipeline.RunAsync("secrets", "set", "hashing-salt", "--replace");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Secrets[SecretName.HashingSalt]).IsEqualTo("synthetic-salt-three");
    }

    [Test]
    public async Task A_stored_hashing_salt_is_replaced_by_a_generated_one_with_the_replace_option()
    {
        await using var pipeline = new PipelineHarness();

        var exit = await pipeline.RunAsync("secrets", "set", "hashing-salt", "--generate", "--replace");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Secrets[SecretName.HashingSalt]).IsNotEqualTo(PipelineHarness.HashingSalt);
        await Assert.That(Convert.FromBase64String(pipeline.Secrets[SecretName.HashingSalt]).Length).IsEqualTo(32);
    }

    [Test]
    public async Task A_blank_stored_hashing_salt_counts_as_not_set()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Secrets[SecretName.HashingSalt] = "  ";
        pipeline.Console.TypeLine("synthetic-salt-three");

        var exit = await pipeline.RunAsync("secrets", "set", "hashing-salt");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Secrets[SecretName.HashingSalt]).IsEqualTo("synthetic-salt-three");
    }

    [Test]
    public async Task A_generated_salt_is_256_random_bits_stored_without_being_asked_for_or_shown()
    {
        await using var first = new PipelineHarness();
        await using var second = new PipelineHarness();
        first.Secrets.Clear();
        second.Secrets.Clear();

        var exit = await first.RunAsync("secrets", "set", "hashing-salt", "--generate");
        await second.RunAsync("secrets", "set", "hashing-salt", "--generate");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(first.Console.KeysRead).IsEqualTo(0);
        var salt = first.Secrets[SecretName.HashingSalt];
        await Assert.That(Convert.FromBase64String(salt).Length).IsEqualTo(32);
        await Assert.That(second.Secrets[SecretName.HashingSalt]).IsNotEqualTo(salt);
        await Assert.That(first.Out.ToString().ReplaceLineEndings("\n")).IsEqualTo("Stored the secret 'hashing salt'.\n");
        await Assert.That(first.Error.ToString()).IsEmpty();
        await Assert.That(first.Console.StandardInputOpened).IsFalse();
    }

    [Test]
    [Arguments("jira-email")]
    [Arguments("jira-api-token")]
    public async Task Only_the_hashing_salt_can_be_generated(string argument)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Secrets.Clear();

        var exit = await pipeline.RunAsync("secrets", "set", argument, "--generate");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("--generate applies only to hashing-salt.\n");
        await Assert.That(pipeline.Secrets).IsEmpty();
        await Assert.That(pipeline.Console.KeysRead).IsEqualTo(0);
    }

    [Test]
    public async Task A_secret_the_tool_does_not_have_is_refused()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Secrets.Clear();
        pipeline.Console.TypeLine("synthetic-api-token-0002");

        var exit = await pipeline.RunAsync("secrets", "set", "github-token");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("github-token");
        await Assert.That(pipeline.Secrets).IsEmpty();
        await Assert.That(pipeline.Console.KeysRead).IsEqualTo(0);
        await Assert.That(pipeline.Error.ToString()).DoesNotContain("Value for the secret");
    }

    [Test]
    public async Task Where_secrets_come_from_environment_variables_secrets_set_stores_nothing_and_names_the_variable()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.SecretStore = new EnvironmentSecretStore(_ => null);
        pipeline.Console.TypeLine("synthetic-api-token-0002");

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "'secrets set' cannot store a secret on this system. Set the environment variable TELLTALE_JIRA_API_TOKEN.\n");
        await Assert.That(pipeline.Console.KeysRead).IsEqualTo(0);
    }

    private const string LostSaltOnRun =
        "The secret 'hashing salt' is not set in the secret store, and the database already holds people hashed with the salt that was there before. "
        + "A new salt would give every person a second hash, so each would count as two. Restore the original salt. Run 'telltale secrets set hashing-salt'.\n";

    private const string LostSaltOnGenerate =
        "The database already holds people hashed with an earlier salt. A new salt would give every person a second hash, so each would count as two. "
        + "Restore the original salt by running 'telltale secrets set hashing-salt' without --generate, or add --replace to start again with a new salt.\n";

    [Test]
    public async Task A_run_with_the_salt_gone_and_hashed_people_in_the_database_says_to_restore_the_original()
    {
        await using var pipeline = new PipelineHarness();
        await RunOnceWithAnAssigneeAsync(pipeline);
        var requestsBefore = pipeline.Jira.Handler.Requests.Count;
        pipeline.Secrets.Remove(SecretName.HashingSalt);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(LostSaltOnRun);
        await Assert.That(pipeline.Error.ToString()).DoesNotContain("--generate");
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(requestsBefore);
    }

    [Test]
    public async Task A_run_with_the_salt_gone_and_no_hashed_people_in_the_database_may_have_a_new_one_generated()
    {
        // Issues without an assignee leave no hash behind, so no stored hash can stop matching.
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).Moved(Local(2026, 1, 7, 9), Workflow.Development).Moved(Local(2026, 1, 9, 9), Workflow.Done);
        await pipeline.RunAsync("run", "--sprint", "41");
        pipeline.Secrets.Remove(SecretName.HashingSalt);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "The secret 'hashing salt' is not set in the secret store. Run 'telltale secrets set hashing-salt'. Add --generate to have a new random salt made.\n");
    }

    [Test]
    public async Task A_salt_is_not_generated_over_hashed_people_in_the_database_unless_asked_to_replace()
    {
        await using var pipeline = new PipelineHarness();
        await RunOnceWithAnAssigneeAsync(pipeline);
        pipeline.Secrets.Remove(SecretName.HashingSalt);

        var exit = await pipeline.RunAsync("secrets", "set", "hashing-salt", "--generate");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(LostSaltOnGenerate);
        await Assert.That(pipeline.Secrets.ContainsKey(SecretName.HashingSalt)).IsFalse();
    }

    [Test]
    public async Task A_salt_is_generated_over_hashed_people_in_the_database_with_the_replace_option()
    {
        await using var pipeline = new PipelineHarness();
        await RunOnceWithAnAssigneeAsync(pipeline);
        pipeline.Secrets.Remove(SecretName.HashingSalt);

        var exit = await pipeline.RunAsync("secrets", "set", "hashing-salt", "--generate", "--replace");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(Convert.FromBase64String(pipeline.Secrets[SecretName.HashingSalt]).Length).IsEqualTo(32);
    }

    [Test]
    public async Task The_original_salt_can_be_typed_back_in_over_hashed_people_in_the_database()
    {
        await using var pipeline = new PipelineHarness();
        await RunOnceWithAnAssigneeAsync(pipeline);
        pipeline.Secrets.Remove(SecretName.HashingSalt);
        pipeline.Console.TypeLine(PipelineHarness.HashingSalt);

        var exit = await pipeline.RunAsync("secrets", "set", "hashing-salt");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Secrets[SecretName.HashingSalt]).IsEqualTo(PipelineHarness.HashingSalt);
    }

    private static async Task RunOnceWithAnAssigneeAsync(PipelineHarness pipeline)
    {
        AddOneSprint(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).AssignedTo(People.One)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);
        var exit = await pipeline.RunAsync("run", "--sprint", "41");
        await Assert.That(exit).IsEqualTo(0);
    }

    private static void AddOneSprint(FakeJira jira) =>
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));

    [Test]
    public async Task No_secret_value_reaches_the_console_the_configuration_file_the_database_or_the_report()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Secrets.Clear();
        AddOneSprint(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);

        pipeline.Console.TypeLine("second.owner@example.invalid");
        await pipeline.RunAsync("secrets", "set", "jira-email");
        pipeline.Console.TypeLine("synthetic-api-token-0002");
        await pipeline.RunAsync("secrets", "set", "jira-api-token");
        await pipeline.RunAsync("secrets", "set", "hashing-salt", "--generate");
        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        var files = await pipeline.ReadAllFilesAsync();

        // The scan is only worth something if it reads what the commands wrote and the secrets were used.
        await Assert.That(files.Keys).Contains("telltale.json");
        await Assert.That(files.Keys).Contains(Path.Combine("reports", "sprint-41.md"));
        await Assert.That(files["store.db"]).Contains(pipeline.Jira.IdOf("FAKE-1"));
        await Assert.That(pipeline.Out.ToString()).Contains("Stored the secret 'hashing salt'.");
        await Assert.That(pipeline.Secrets.Count).IsEqualTo(3);

        var everything = string.Concat(files.Values) + pipeline.Out + pipeline.Error;
        foreach (var value in pipeline.Secrets.Values)
        {
            await Assert.That(everything.Contains(value, StringComparison.Ordinal)).IsFalse();
        }
    }

    [Test]
    public async Task Secrets_set_needs_no_configuration_file_and_creates_no_database()
    {
        // It is the first command run on a new machine.
        await using var pipeline = new PipelineHarness();
        pipeline.RawConfig = "not a configuration file";
        pipeline.Console.TypeLine("synthetic-api-token-0002");

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(File.Exists(pipeline.DatabasePath)).IsFalse();
    }
}
