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
    [Arguments("description")]
    [Arguments("customfield_")]
    [Arguments("customfield_10016 ")]
    public async Task A_story_point_field_id_that_is_not_a_custom_field_id_is_refused(string fieldId)
    {
        // The field is asked for by name: "description" would have Jira send free text.
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Config["jira"]!["storyPointFieldId"] = fieldId;

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json is not valid: jira.storyPointFieldId must be a custom field ID such as customfield_10016.");
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
    [Arguments("timeZone")]
    [Arguments("holidays")]
    [Arguments("statusRoles")]
    [Arguments("jira")]
    public async Task A_required_setting_given_as_null_is_refused_and_named(string setting)
    {
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Config[setting] = null;

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json is not valid").And.Contains($"'{setting}'").And.Contains("null");
        await Assert.That(pipeline.Error.ToString()).DoesNotContain("   at ");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    [Arguments("http://example.invalid/")]
    [Arguments("/rest/relative")]
    [Arguments("example.invalid")]
    public async Task A_site_url_that_is_not_an_absolute_https_address_is_refused(string siteUrl)
    {
        // Over http the Jira email and API token would travel in clear text.
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Config["jira"]!["siteUrl"] = siteUrl;

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json is not valid: jira.siteUrl must be an absolute https:// address.");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    public async Task A_site_url_that_carries_a_user_name_or_password_is_refused_and_not_shown()
    {
        // The file holds no secrets, and a password in the address is one.
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Config["jira"]!["siteUrl"] = "https://fake.user:synthetic-password@example.invalid/";

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json is not valid: jira.siteUrl must not carry a user name or password.");
        await Assert.That(pipeline.Error.ToString()).DoesNotContain("synthetic-password");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    [Arguments("missing")]
    [Arguments("null")]
    [Arguments("empty")]
    public async Task A_configuration_without_project_keys_is_refused_and_names_the_setting(string how)
    {
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        var jira = pipeline.Config["jira"]!.AsObject();
        switch (how)
        {
            case "missing":
                jira.Remove("projectKeys");
                break;
            case "null":
                jira["projectKeys"] = null;
                break;
            default:
                jira["projectKeys"] = new System.Text.Json.Nodes.JsonArray();
                break;
        }

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json is not valid").And.Contains("projectKeys");
        await Assert.That(pipeline.Error.ToString()).DoesNotContain("   at ");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    [Arguments("FAKE\") OR project = \"SECRET")]
    [Arguments("FAKE\"")]
    [Arguments("FAKE, SECRET")]
    [Arguments("fake")]
    [Arguments(" FAKE")]
    [Arguments("1FAKE")]
    [Arguments("")]
    public async Task A_project_key_that_is_not_in_jiras_key_form_is_refused_before_any_search(string key)
    {
        // The keys are written into the JQL, so only text that can be nothing but a key is accepted.
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Config["jira"]!["projectKeys"] = new System.Text.Json.Nodes.JsonArray("FAKE", key);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains(
            $"telltale.json is not valid: jira.projectKeys: '{key}' is not a Jira project key (an upper-case letter followed by upper-case letters, digits or underscores).");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    [Arguments("FAKE")]
    [Arguments("F2")]
    [Arguments("FAKE_TEAM9")]
    public async Task A_project_key_in_jiras_key_form_is_accepted(string key)
    {
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Config["jira"]!["projectKeys"] = new System.Text.Json.Nodes.JsonArray(key);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var search = pipeline.Jira.Handler.Requests.Single(request => request.Path == "/rest/api/3/search/jql");
        using var body = System.Text.Json.JsonDocument.Parse(search.Body);
        await Assert.That(body.RootElement.GetProperty("jql").GetString()).StartsWith($"project in (\"{key}\") AND ");
    }

    [Test]
    public async Task A_site_url_with_a_path_and_no_trailing_slash_keeps_its_path()
    {
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Config["jira"]!["siteUrl"] = "https://example.invalid/tenant";

        // The fake serves nothing under /tenant, so the run fails; what matters is where the request went.
        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Jira.Handler.Requests.Select(request => request.Path)).IsEquivalentTo(new[] { "/tenant/rest/api/3/status" });
    }

    [Test]
    public async Task A_setting_written_twice_is_refused()
    {
        // With two values for one setting, the reader of the file cannot tell which one is in force.
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.RawConfig = pipeline.Config.ToJsonString().Replace("\"timeZone\":", "\"timeZone\":\"Europe/Paris\",\"timeZone\":", StringComparison.Ordinal);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json is not valid").And.Contains("timeZone");
        await Assert.That(pipeline.Error.ToString()).DoesNotContain("   at ");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    [Arguments("")]
    [Arguments("  ")]
    public async Task An_empty_story_point_field_id_is_refused(string fieldId)
    {
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Config["jira"]!["storyPointFieldId"] = fieldId;

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json is not valid: jira.storyPointFieldId must not be empty.");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    [Arguments(0L)]
    [Arguments(-400001L)]
    public async Task A_board_id_that_is_not_positive_is_refused(long boardId)
    {
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Config["jira"]!["boardId"] = boardId;

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json is not valid: jira.boardId must be a positive number.");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    public async Task A_run_that_stops_for_a_missing_secret_leaves_no_database_behind()
    {
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Secrets.Remove(SecretName.JiraApiToken);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("The secret 'Jira API token' is not set");
        await Assert.That(File.Exists(pipeline.DatabasePath)).IsFalse();
    }

    [Test]
    public async Task A_status_role_map_with_no_done_status_is_refused()
    {
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Config["statusRoles"]![Workflow.Done.Id] = "work";
        pipeline.Config["statusRoles"]![Workflow.Shipped.Id] = "work";

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json is not valid: statusRoles: no status has the role 'done', so nothing could ever be delivered.");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    public async Task A_status_an_issue_has_been_in_that_has_no_role_stops_the_run_and_is_named()
    {
        // Every status has one role. Guessing "not done" for a new or mistyped status would quietly drop deliveries.
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Config["statusRoles"]!.AsObject().Remove(Workflow.Review.Id);
        pipeline.Config["statusRoles"]!.AsObject().Remove(Workflow.Shipped.Id);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Review)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done)
            .Moved(Local(2026, 1, 10, 9), Workflow.Shipped);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "These statuses have no role in statusRoles in the configuration file: 600005, 600009. Give each one a role (not started, work, blocked or done) and run again.\n");
        await Assert.That(File.Exists(Path.Combine(pipeline.ReportDirectory, "sprint-41.md"))).IsFalse();
    }

    [Test]
    public async Task A_status_with_no_role_that_an_issue_entered_and_left_at_the_same_instant_stops_the_run_and_is_named()
    {
        // The visit took no time, but it is a move: read as "not done", it would be a reopen and a later delivery.
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Config["statusRoles"]!.AsObject().Remove(Workflow.Shipped.Id);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done)
            .Moved(Local(2026, 1, 12, 9), Workflow.Shipped)
            .Moved(Local(2026, 1, 12, 9), Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "These statuses have no role in statusRoles in the configuration file: 600009. Give each one a role (not started, work, blocked or done) and run again.\n");
        await Assert.That(File.Exists(Path.Combine(pipeline.ReportDirectory, "sprint-41.md"))).IsFalse();
    }

    [Test]
    public async Task A_status_with_no_role_that_a_change_only_names_as_the_status_moved_from_stops_the_run_and_is_named()
    {
        // The changelog says the issue left Review, though no change ever put it there (history Jira lost).
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Config["statusRoles"]!.AsObject().Remove(Workflow.Review.Id);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .NowIn(Workflow.Review)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "These statuses have no role in statusRoles in the configuration file: 600005. Give each one a role (not started, work, blocked or done) and run again.\n");
    }

    [Test]
    public async Task A_status_no_issue_has_been_in_needs_no_role()
    {
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Config["statusRoles"]!.AsObject().Remove(Workflow.Blocked.Id);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 1\n");
    }

    [Test]
    public async Task A_configuration_without_a_holiday_list_is_refused()
    {
        // ADR-0001: the holiday list is required. Left out, every holiday would silently count as a business day.
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Config.Remove("holidays");

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json is not valid").And.Contains("holidays");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    [Arguments("19 January 2026")]
    [Arguments("2026-02-30")]
    [Arguments("2026-01-19T00:00:00Z")]
    public async Task A_holiday_that_is_not_a_calendar_date_is_refused(string holiday)
    {
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Config["holidays"] = new System.Text.Json.Nodes.JsonArray("2026-01-01", holiday);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json is not valid").And.Contains("holidays[1]");
        await Assert.That(pipeline.Error.ToString()).DoesNotContain("   at ");
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
    [Arguments(SecretName.HashingSalt, "'hashing salt'", "hashing-salt'. Add --generate to have a new random salt made.")]
    [Arguments(SecretName.JiraEmail, "'Jira email'", "jira-email'.")]
    [Arguments(SecretName.JiraApiToken, "'Jira API token'", "jira-api-token'.")]
    public async Task A_missing_secret_stops_the_run_before_any_call_to_jira_and_is_named(SecretName missing, string expectedName, string howToSet)
    {
        await using var pipeline = new PipelineHarness();
        AddOneSprint(pipeline.Jira);
        pipeline.Secrets.Remove(missing);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo($"The secret {expectedName} is not set in the secret store. Run 'telltale secrets set {howToSet}\n");
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
