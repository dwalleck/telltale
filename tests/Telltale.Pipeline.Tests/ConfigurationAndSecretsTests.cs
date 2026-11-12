using Telltale.Cli;
using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

public class ConfigurationAndSecretsTests
{
    [Test]
    public async Task A_configuration_file_with_a_setting_the_tool_does_not_know_is_refused()
    {
        // The file has no place for a secret: one pasted into it stops the run.
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Config["jira"]!["apiToken"] = "synthetic-pasted-token";

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json is not valid").And.Contains("apiToken");
        await Assert.That(pipeline.Error.ToString()).DoesNotContain("synthetic-pasted-token");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    public async Task A_status_role_outside_the_four_roles_is_refused()
    {
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Config["statusRoles"]![Workflow.Done.Id] = "finished";

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json is not valid: statusRoles: 'finished' is not a status role.");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    public async Task An_unknown_time_zone_is_refused()
    {
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Config["timeZone"] = "Nowhere/Invalid";

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("timeZone: 'Nowhere/Invalid' is not a known IANA time zone ID.");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    [Arguments(SecretName.HashingSalt, "'hashing salt'")]
    [Arguments(SecretName.JiraEmail, "'Jira email'")]
    [Arguments(SecretName.JiraApiToken, "'Jira API token'")]
    public async Task A_missing_secret_stops_the_run_before_any_call_to_jira_and_is_named(SecretName missing, string expectedName)
    {
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Secrets.Remove(missing);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo($"The secret {expectedName} is not set in the secret store.\n");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    public async Task A_blank_hashing_salt_is_refused()
    {
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Secrets[SecretName.HashingSalt] = " ";

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("'hashing salt' is not set");
    }

    [Test]
    public async Task Jira_is_called_with_the_email_and_api_token_from_the_secret_store()
    {
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);

        await pipeline.RunAsync("run", "--sprint", "41");

        // Base64 of "tool.owner@example.invalid:synthetic-api-token-0001", computed with the base64 command.
        const string expected = "Basic dG9vbC5vd25lckBleGFtcGxlLmludmFsaWQ6c3ludGhldGljLWFwaS10b2tlbi0wMDAx";
        var sent = pipeline.Jira.Handler.Requests.Select(request => request.Headers.Authorization?.ToString() ?? "(none)").Distinct().ToList();
        await Assert.That(sent).IsEquivalentTo(new[] { expected });
    }

    [Test]
    public async Task The_environment_secret_store_reads_one_variable_per_secret()
    {
        var variables = new Dictionary<string, string>
        {
            ["TELLTALE_JIRA_EMAIL"] = "tool.owner@example.invalid",
            ["TELLTALE_JIRA_API_TOKEN"] = "synthetic-api-token-0001",
            ["TELLTALE_HASHING_SALT"] = "synthetic-salt-one",
        };
        var store = new EnvironmentSecretStore(variables.GetValueOrDefault);

        await Assert.That(store.Get(SecretName.JiraEmail)).IsEqualTo("tool.owner@example.invalid");
        await Assert.That(store.Get(SecretName.JiraApiToken)).IsEqualTo("synthetic-api-token-0001");
        await Assert.That(store.Get(SecretName.HashingSalt)).IsEqualTo("synthetic-salt-one");
    }

    [Test]
    public async Task The_environment_secret_store_reports_an_unset_variable_as_missing()
    {
        var store = new EnvironmentSecretStore(_ => null);

        await Assert.That(store.Get(SecretName.HashingSalt)).IsNull();
    }

    private static void AddOneSprint(FakeJira jira) =>
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
}
