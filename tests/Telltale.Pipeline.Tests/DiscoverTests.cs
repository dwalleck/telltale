using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Telltale.Cli;
using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

public class DiscoverTests
{
    /// <summary>The first <c>discover</c> on a machine: no configuration file yet, so everything Jira cannot tell is given.</summary>
    internal static readonly string[] FirstDiscover =
    [
        "discover", "--site-url", FakeJira.SiteUrl, "--board", "400001", "--time-zone", ZoneId, "--backfill-start", "2025-01-01",
    ];

    /// <summary>What discover says before the failure itself when its first call, the connection check, fails.</summary>
    private const string FirstCallFailed =
        "discover's first call to Jira failed. When this call fails, the likely cause is the system proxy or trust in the corporate root certificate. The failure was: ";

    [Test]
    public async Task Run_accepts_the_file_discover_wrote_without_any_edit()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).AssignedTo(People.One)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 15), Workflow.Done);
        pipeline.Jira.AddIdleTeam(41);

        var discovered = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        var written = await File.ReadAllTextAsync(pipeline.ConfigPath);
        var ran = await pipeline.RunWithFilesAsTheyAreAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(discovered).IsEqualTo(0);
        await Assert.That(ran).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("Delivered issues: 1\n");
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(written);
    }

    [Test]
    public async Task Every_status_is_listed_with_its_id_name_jira_category_and_proposed_role()
    {
        await using var pipeline = new PipelineHarness();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(Output(pipeline)).Contains(
            "Statuses (ID, name, Jira category, role):\n"
            + "  600001  Fake New  new  not started (proposed)\n"
            + "  600002  Fake Approved  new  not started (proposed)\n"
            + "  600003  Fake Committed  new  not started (proposed)\n"
            + "  600004  Fake Development  indeterminate  work (proposed)\n"
            + "  600005  Fake Review  indeterminate  work (proposed)\n"
            + "  600006  Fake QE  indeterminate  work (proposed)\n"
            + "  600007  Fake Blocked  indeterminate  blocked (proposed)\n"
            + "  600008  Fake Done  done  done (proposed)\n"
            + "  600009  Fake Shipped  done  done (proposed)\n");
    }

    [Test]
    public async Task Every_resolution_is_listed_with_its_id_and_name()
    {
        await using var pipeline = new PipelineHarness();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(Output(pipeline)).Contains("Resolutions (ID, name):\n  300001  Fake Done\n  300002  Fake Won't Do\n");
    }

    [Test]
    public async Task The_sprint_and_story_point_fields_are_found_by_their_type_in_the_field_list()
    {
        // Other IDs than the default fixture's, and names that say nothing: only the field type can find them.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Fields.Clear();
        pipeline.Jira.Fields.AddRange(
        [
            new FakeField("customfield_10555", "Fake Budget", "number", FakeJira.NumberFieldType),
            new FakeField("customfield_10777", "Fake Iteration", "array", FakeJira.SprintFieldType),
            new FakeField("customfield_10888", "Fake Size", "number", FakeJira.StoryPointEstimateFieldType),
            new FakeField("summary", "Summary", "string", null),
        ]);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(Output(pipeline)).Contains("Sprint field: customfield_10777 (Fake Iteration)\n");
        await Assert.That(Output(pipeline)).Contains("Story point field: customfield_10888 (Fake Size)\n");
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["jira"]!["storyPointFieldId"]!.GetValue<string>()).IsEqualTo("customfield_10888");
    }

    [Test]
    [Arguments(HttpRequestError.SecureConnectionError)]
    [Arguments(HttpRequestError.ProxyTunnelError)]
    public async Task A_certificate_or_proxy_failure_on_the_first_call_names_the_proxy_and_the_root_certificate_and_writes_nothing(HttpRequestError failure)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/rest/api/3/status", _ => true,
            _ => throw new HttpRequestException(failure, "synthetic connection failure"));

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            FirstCallFailed
            + $"Could not reach Jira (GET rest/api/3/status): {failure}. "
            + "On the corporate network this usually means the proxy or the certificate: check that the system proxy is set and that the corporate root certificate is trusted.\n");

        // The check is the first call and the only one: nothing else is asked of a site that cannot be reached.
        await Assert.That(pipeline.Jira.Handler.Requests.Select(request => request.Path)).IsEquivalentTo(["/rest/api/3/status"]);
        await Assert.That(File.Exists(pipeline.ConfigPath)).IsFalse();
    }

    [Test]
    [Arguments(JiraShapes.AsSchemas)]
    [Arguments(JiraShapes.AsExamples)]
    public async Task The_keys_of_all_the_boards_projects_are_written_as_project_keys_however_jira_pages_them(JiraShapes shapes)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Shapes = shapes;
        pipeline.Jira.OffsetPageSize = 1;
        pipeline.Jira.BoardProjectKeys.Add("FAKE2");

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["jira"]!["projectKeys"]!.AsArray().Select(key => key!.GetValue<string>())).IsEquivalentTo(["FAKE", "FAKE2"]);
        await Assert.That(Output(pipeline)).Contains("Project keys: FAKE, FAKE2\n");
    }

    [Test]
    public async Task Resolutions_on_a_later_page_are_listed_too()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.OffsetPageSize = 1;

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(Output(pipeline)).Contains("Resolutions (ID, name):\n  300001  Fake Done\n  300002  Fake Won't Do\n");
    }

    [Test]
    public async Task Run_never_asks_jira_for_the_boards_projects()
    {
        // Owner decision D2: the collector reads the project keys from the configuration on every sync.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Jira.Handler.Requests.Select(request => request.Path)).DoesNotContain(pipeline.Jira.BoardProjectPath);
    }

    [Test]
    public async Task Discover_writes_no_secret_and_no_description_to_the_file_or_the_console()
    {
        await using var pipeline = new PipelineHarness();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(0);
        var files = await pipeline.ReadAllFilesAsync();
        await Assert.That(files.Keys).IsEquivalentTo(["telltale.json"]);

        string[] forbidden = [PipelineHarness.JiraEmail, PipelineHarness.JiraApiToken, PipelineHarness.HashingSalt, .. pipeline.Jira.ForbiddenText];
        var written = files.Select(file => (Where: file.Key, Text: file.Value))
            .Append((Where: "standard output", Text: pipeline.Out.ToString()))
            .Append((Where: "standard error", Text: pipeline.Error.ToString()));
        var leaks = (from place in written
                     from text in forbidden
                     where place.Text.Contains(text, StringComparison.OrdinalIgnoreCase)
                     select $"forbidden text in {place.Where}").ToList();
        await Assert.That(leaks).IsEmpty();
    }

    [Test]
    public async Task A_rerun_keeps_the_roles_the_owner_edited_and_says_where_the_proposal_differs()
    {
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config =>
        {
            config["statusRoles"]![Workflow.Blocked.Id] = "work";
            config["statusRoles"]![Workflow.Committed.Id] = "work";
        });
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var roles = (await ReadWrittenConfigAsync(pipeline))["statusRoles"]!;
        await Assert.That(roles[Workflow.Blocked.Id]!.GetValue<string>()).IsEqualTo("work");
        await Assert.That(roles[Workflow.Committed.Id]!.GetValue<string>()).IsEqualTo("work");
        await Assert.That(roles[Workflow.Done.Id]!.GetValue<string>()).IsEqualTo("done");
        await Assert.That(Output(pipeline)).Contains("  600007  Fake Blocked  indeterminate  work (from the file; discover proposes blocked)\n");
        await Assert.That(Output(pipeline)).Contains("  600003  Fake Committed  new  work (from the file; discover proposes not started)\n");
        await Assert.That(Output(pipeline)).Contains("  600008  Fake Done  done  done (from the file)\n");
    }

    [Test]
    public async Task A_rerun_without_options_keeps_the_site_board_time_zone_and_backfill_start_of_the_file()
    {
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config =>
        {
            config["timeZone"] = "Europe/London";
            config["jira"]!["backfillStart"] = "2024-06-01";
        });

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["timeZone"]!.GetValue<string>()).IsEqualTo("Europe/London");
        await Assert.That(written["jira"]!["backfillStart"]!.GetValue<string>()).IsEqualTo("2024-06-01");
        await Assert.That(written["jira"]!["siteUrl"]!.GetValue<string>()).IsEqualTo(FakeJira.SiteUrl);
        await Assert.That(written["jira"]!["boardId"]!.GetValue<long>()).IsEqualTo(FakeJira.BoardId);
    }

    [Test]
    public async Task An_option_given_on_a_rerun_replaces_the_files_value()
    {
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover", "--time-zone", "Europe/London", "--backfill-start", "2024-06-01");

        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["timeZone"]!.GetValue<string>()).IsEqualTo("Europe/London");
        await Assert.That(written["jira"]!["backfillStart"]!.GetValue<string>()).IsEqualTo("2024-06-01");
    }

    [Test]
    public async Task Only_the_boards_statuses_are_written_in_the_order_of_the_boards_columns()
    {
        // Owner decision D29. QE is in the site's status list but mapped to no column of this board, and the done column comes first.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.BoardColumns.Clear();
        pipeline.Jira.BoardColumns.AddRange(
        [
            new FakeColumn("Fake Done", [Workflow.Shipped.Id, Workflow.Done.Id]),
            new FakeColumn("Fake Doing", [Workflow.Review.Id, Workflow.Development.Id]),
            new FakeColumn("Fake Empty", []),
            new FakeColumn("Fake To Do", [Workflow.New.Id]),
        ]);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(0);
        var roles = (await ReadWrittenConfigAsync(pipeline))["statusRoles"]!.AsObject();
        await Assert.That(string.Join(" ", roles.Select(role => $"{role.Key}={role.Value}"))).IsEqualTo("600009=done 600008=done 600005=work 600004=work 600001=not started");
        await Assert.That(Output(pipeline)).Contains(
            "Statuses (ID, name, Jira category, role):\n"
            + "  600009  Fake Shipped  done  done (proposed)\n"
            + "  600008  Fake Done  done  done (proposed)\n"
            + "  600005  Fake Review  indeterminate  work (proposed)\n"
            + "  600004  Fake Development  indeterminate  work (proposed)\n"
            + "  600001  Fake New  new  not started (proposed)\n"
            + "Resolutions");
    }

    [Test]
    public async Task A_rerun_never_adds_a_board_status_the_file_leaves_out_and_says_the_board_has_it()
    {
        // Owner decision D29: the owner may have removed it. Review was removed by the owner; QE came onto the board later.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.BoardColumns[1].StatusIds.Remove(Workflow.QE.Id);
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config => config["statusRoles"]!.AsObject().Remove(Workflow.Review.Id));
        pipeline.Jira.BoardColumns[1].StatusIds.Add(Workflow.QE.Id);
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover", "--time-zone", "Europe/London");

        await Assert.That(exit).IsEqualTo(0);
        var roles = (await ReadWrittenConfigAsync(pipeline))["statusRoles"]!.AsObject();
        await Assert.That(roles.Select(role => role.Key)).IsEquivalentTo(["600001", "600002", "600003", "600004", "600007", "600008", "600009"]);
        await Assert.That(Output(pipeline)).Contains("  600005  Fake Review  indeterminate  left out of the file (the board has this status; give it a role in the file if issues use it)\n");
        await Assert.That(Output(pipeline)).Contains("  600006  Fake QE  indeterminate  left out of the file (the board has this status; give it a role in the file if issues use it)\n");
    }

    [Test]
    public async Task A_rerun_keeps_the_role_of_a_status_that_is_not_on_the_board_and_says_so()
    {
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config => config["statusRoles"]!["600077"] = "blocked");
        pipeline.Jira.BoardColumns[1].StatusIds.Remove(Workflow.QE.Id);
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover", "--time-zone", "Europe/London");

        await Assert.That(exit).IsEqualTo(0);
        var roles = (await ReadWrittenConfigAsync(pipeline))["statusRoles"]!;
        await Assert.That(roles["600077"]!.GetValue<string>()).IsEqualTo("blocked");
        await Assert.That(roles[Workflow.QE.Id]!.GetValue<string>()).IsEqualTo("work");
        await Assert.That(Output(pipeline)).Contains("  600006  Fake QE  indeterminate  work (from the file; not on the board)\n");
        await Assert.That(Output(pipeline)).Contains("  600077  (not in Jira's status list)  -  blocked (from the file; not on the board)\n");
    }

    [Test]
    [Arguments("Fake BLOCKED by QE", "indeterminate")]
    [Arguments("Fake Unblocking", "done")]
    public async Task A_status_whose_name_contains_block_in_any_case_is_proposed_as_blocked_whatever_its_category(string name, string category)
    {
        // Owner decision D30.
        await using var pipeline = new PipelineHarness();
        var status = category == "done" ? Workflow.Shipped : Workflow.Review;
        pipeline.Jira.RenameStatus(status, name);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That((await ReadWrittenConfigAsync(pipeline))["statusRoles"]![status.Id]!.GetValue<string>()).IsEqualTo("blocked");
        await Assert.That(Output(pipeline)).Contains($"  {status.Id}  {name}  {category}  blocked (proposed)\n");
    }

    [Test]
    [Arguments("""{ "id": 400001, "name": "Fake Board" }""", "KeyNotFoundException")]
    [Arguments("""{ "columnConfig": { "columns": { "name": "Fake To Do" } } }""", "InvalidOperationException")]
    [Arguments("""{ "columnConfig": { "columns": [ { "name": "Fake To Do", "statuses": [ { "self": "https://example.invalid/rest/api/3/status/600001" } ] } ] } }""", "KeyNotFoundException")]
    [Arguments("""{ "columnConfig": { "columns": [ { "name": "Fake To Do", "statuses": [ { "id": 600001 } ] } ] } }""", "InvalidOperationException")]
    public async Task A_board_configuration_in_an_unknown_shape_stops_discover_plainly_and_writes_nothing(string answer, string kind)
    {
        // The endpoint's shape is known from the documentation only.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Handler.Override(HttpMethod.Get, pipeline.Jira.BoardConfigurationPath, _ => true, StubHttpHandler.Json(answer));

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"Jira's board configuration answered in a shape this tool does not understand ({kind}). Nothing was written.\n");
        await Assert.That(File.Exists(pipeline.ConfigPath)).IsFalse();
    }

    [Test]
    public async Task A_file_with_a_setting_run_would_refuse_is_refused_by_a_rerun_and_neither_rewritten_nor_copied()
    {
        // The file has no place for a secret. One pasted into it must not be written again or copied into a backup.
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config =>
        {
            config["jira"]!["apiToken"] = "synthetic-pasted-token";
            config["timeZone"] = "Europe/London";
        });
        var edited = await File.ReadAllTextAsync(pipeline.ConfigPath);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("run would refuse it").And.Contains("apiToken");
        await Assert.That(pipeline.Error.ToString() + pipeline.Out).DoesNotContain("synthetic-pasted-token");
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(edited);
        await Assert.That((await pipeline.ReadAllFilesAsync()).Keys).IsEquivalentTo(["telltale.json"]);
    }

    [Test]
    public async Task A_rerun_that_changes_the_file_keeps_the_previous_one_under_a_dated_name_and_says_its_comments_are_there()
    {
        // The harness clock reads 3 February 2026, noon in Chicago: 18:00 UTC.
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        var edited = (await File.ReadAllTextAsync(pipeline.ConfigPath)).Replace("{", "{ // the owner's note", StringComparison.Ordinal);
        await File.WriteAllTextAsync(pipeline.ConfigPath, edited);
        var backup = pipeline.ConfigPath + ".20260203T180000Z.bak";

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover", "--time-zone", "Europe/London");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await File.ReadAllTextAsync(backup)).IsEqualTo(edited);
        await Assert.That(Output(pipeline)).Contains($"The previous file is kept as {backup}.\n");
        await Assert.That(Output(pipeline)).Contains($"The previous file had comments, and the new one has none: they are only in {backup}.\n");
    }

    [Test]
    public async Task A_second_rerun_does_not_overwrite_the_backup_of_the_first()
    {
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        var first = await File.ReadAllTextAsync(pipeline.ConfigPath);
        await pipeline.RunWithFilesAsTheyAreAsync("discover", "--time-zone", "Europe/London");
        var second = await File.ReadAllTextAsync(pipeline.ConfigPath);

        // The clock has not moved, so the second backup would take the first one's name.
        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover", "--time-zone", "Europe/Paris");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath + ".20260203T180000Z.bak")).IsEqualTo(first);
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath + ".20260203T180000Z-2.bak")).IsEqualTo(second);
        await Assert.That(Output(pipeline)).DoesNotContain("had comments");
    }

    [Test]
    public async Task A_rerun_keeps_the_project_keys_of_the_file_and_says_what_jira_lists()
    {
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        pipeline.Jira.BoardProjectKeys.Add("FAKE2");
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover");

        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["jira"]!["projectKeys"]!.ToJsonString()).IsEqualTo("""["FAKE"]""");
        await Assert.That(Output(pipeline)).Contains("Project keys: FAKE (from the file; Jira lists FAKE, FAKE2)\n");
    }

    [Test]
    public async Task A_rerun_says_which_setting_an_option_changed_and_never_shows_an_old_site_address()
    {
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config => config["jira"]!["siteUrl"] = "https://old.example.invalid/");
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover", "--site-url", FakeJira.SiteUrl, "--time-zone", "Europe/London");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(Output(pipeline)).Contains("""Changed timeZone: was "America/Chicago", now "Europe/London".""" + "\n");
        await Assert.That(Output(pipeline)).Contains("Changed jira.siteUrl (the old address is not shown).\n");
        await Assert.That(Output(pipeline)).DoesNotContain("old.example.invalid");
        await Assert.That(Output(pipeline)).DoesNotContain("Changed jira.boardId");
    }

    [Test]
    public async Task A_file_whose_site_address_carries_a_password_is_refused_even_when_a_new_address_is_given()
    {
        // Otherwise the password would be copied into the backup.
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config => config["jira"]!["siteUrl"] = "https://fake.user:synthetic-password@example.invalid/");
        var edited = await File.ReadAllTextAsync(pipeline.ConfigPath);
        var requestsBefore = pipeline.Jira.Handler.Requests.Count;

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover", "--site-url", FakeJira.SiteUrl);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"jira.siteUrl in {pipeline.ConfigPath} must not carry a user name or password. Take them out of the file, then run discover again.\n");
        await Assert.That(pipeline.Out.ToString()).DoesNotContain("synthetic-password");
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(requestsBefore);
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(edited);
        await Assert.That((await pipeline.ReadAllFilesAsync()).Keys).IsEquivalentTo(["telltale.json"]);
    }

    [Test]
    [Arguments("this is not JSON")]
    [Arguments("""{ "jira": "not an object" }""")]
    [Arguments("""{ "statusRoles": ["not an object"] }""")]
    [Arguments("""["not an object"]""")]
    [Arguments("""{ "timeZone": "Etc/UTC", "timeZone": "Europe/London" }""")]
    public async Task A_file_discover_cannot_read_is_left_untouched_and_jira_is_not_called(string content)
    {
        await using var pipeline = new PipelineHarness();
        await File.WriteAllTextAsync(pipeline.ConfigPath, content);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json").And.Contains("was left as it is");
        await Assert.That(pipeline.Error.ToString()).DoesNotContain("   at ");
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(content);
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    public async Task The_first_discover_names_every_option_it_still_needs()
    {
        await using var pipeline = new PipelineHarness();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover", "--board", "400001");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"discover needs --site-url, --time-zone and --backfill-start: the configuration file {pipeline.ConfigPath} does not give them yet.\n");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
        await Assert.That(File.Exists(pipeline.ConfigPath)).IsFalse();
    }

    [Test]
    [Arguments("--site-url", "http://example.invalid/", "--site-url must be an absolute https:// address.")]
    [Arguments("--site-url", "example.invalid", "--site-url must be an absolute https:// address.")]
    [Arguments("--site-url", "https://fake.user:synthetic-password@example.invalid/", "--site-url must not carry a user name or password.")]
    [Arguments("--board", "0", "--board must be a positive number.")]
    [Arguments("--time-zone", "Fake/Nowhere", "--time-zone: 'Fake/Nowhere' is not a known IANA time zone ID.")]
    [Arguments("--backfill-start", "01/02/2025", "--backfill-start must be a date written as yyyy-MM-dd.")]
    [Arguments("--backfill-start", "2025-02-30", "--backfill-start must be a date written as yyyy-MM-dd.")]
    public async Task An_option_run_would_refuse_is_refused_before_any_call_to_jira(string option, string value, string message)
    {
        await using var pipeline = new PipelineHarness();
        string[] args = [.. FirstDiscover];
        args[Array.IndexOf(args, option) + 1] = value;

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(args);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(message + "\n");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
        await Assert.That(File.Exists(pipeline.ConfigPath)).IsFalse();
    }

    [Test]
    public async Task A_number_field_named_story_points_is_recognised_and_two_story_point_fields_need_the_owners_choice()
    {
        // A site often has both: "Story Points" (an ordinary number field) and "Story point estimate". The board uses one.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Fields.Add(new FakeField("customfield_10026", "Story Points", "number", FakeJira.NumberFieldType));

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Jira lists more than one story point field: customfield_10016 (Fake Story point estimate), customfield_10026 (Story Points). "
            + "Say which one the board uses with --story-point-field. Nothing was written.\n");
        await Assert.That(File.Exists(pipeline.ConfigPath)).IsFalse();
    }

    [Test]
    public async Task The_story_point_field_the_owner_names_is_written()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Fields.Add(new FakeField("customfield_10026", "Story Points", "number", FakeJira.NumberFieldType));

        var exit = await pipeline.RunWithFilesAsTheyAreAsync([.. FirstDiscover, "--story-point-field", "customfield_10026"]);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That((await ReadWrittenConfigAsync(pipeline))["jira"]!["storyPointFieldId"]!.GetValue<string>()).IsEqualTo("customfield_10026");
        await Assert.That(Output(pipeline)).Contains("Story point field: customfield_10026 (given with --story-point-field)\n");
    }

    [Test]
    public async Task A_rerun_keeps_the_story_point_field_of_the_file()
    {
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config => config["jira"]!["storyPointFieldId"] = "customfield_10026");
        pipeline.Jira.Fields.Add(new FakeField("customfield_10026", "Fake Points", "number", FakeJira.NumberFieldType));
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That((await ReadWrittenConfigAsync(pipeline))["jira"]!["storyPointFieldId"]!.GetValue<string>()).IsEqualTo("customfield_10026");
        await Assert.That(Output(pipeline)).Contains("Story point field: customfield_10026 (from the file; Jira lists customfield_10016 (Fake Story point estimate))\n");
        await Assert.That(Output(pipeline)).DoesNotContain("Changed jira.storyPointFieldId");
    }

    [Test]
    public async Task A_field_list_without_a_story_point_field_stops_discover_and_names_the_option()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Fields.RemoveAll(field => field.Id == FakeJira.StoryPointFieldId);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Jira's field list has no field this tool recognises as story points. Give the field's ID with --story-point-field. Nothing was written.\n");
        await Assert.That(File.Exists(pipeline.ConfigPath)).IsFalse();
    }

    [Test]
    public async Task A_field_list_without_a_sprint_field_stops_discover_and_names_the_option()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Fields.RemoveAll(field => field.Id == FakeJira.SprintFieldId);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Jira's field list has no Sprint field. Check that the site has Jira Software and that this account may see the field, "
            + "or give the field's ID with --sprint-field. Nothing was written.\n");
        await Assert.That(File.Exists(pipeline.ConfigPath)).IsFalse();
    }

    [Test]
    public async Task Two_sprint_fields_need_the_owners_choice()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Fields.Add(new FakeField("customfield_10021", "Fake Other Sprint", "array", FakeJira.SprintFieldType));

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Jira lists more than one Sprint field: customfield_10020 (Fake Sprint), customfield_10021 (Fake Other Sprint). "
            + "Say which one the board uses with --sprint-field. Nothing was written.\n");
        await Assert.That(File.Exists(pipeline.ConfigPath)).IsFalse();
    }

    [Test]
    public async Task The_sprint_field_the_owner_names_is_written_and_a_rerun_keeps_it()
    {
        // The field list shows this field without the Sprint type, so only the owner can name it.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Fields.Add(new FakeField("customfield_10099", "Fake Iteration", "array", null));

        var exit = await pipeline.RunWithFilesAsTheyAreAsync([.. FirstDiscover, "--sprint-field", "customfield_10099"]);
        var rerun = await pipeline.RunWithFilesAsTheyAreAsync("discover", "--time-zone", "Europe/London");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(rerun).IsEqualTo(0);
        await Assert.That((await ReadWrittenConfigAsync(pipeline))["jira"]!["sprintFieldId"]!.GetValue<string>()).IsEqualTo("customfield_10099");
        await Assert.That(Output(pipeline)).Contains("Sprint field: customfield_10099 (given with --sprint-field)\n");
        await Assert.That(Output(pipeline)).Contains("Sprint field: customfield_10099 (from the file; Jira lists customfield_10020 (Fake Sprint))\n");
    }

    [Test]
    public async Task A_sprint_field_that_is_also_the_story_point_field_is_refused_as_run_would()
    {
        await using var pipeline = new PipelineHarness();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync([.. FirstDiscover, "--sprint-field", FakeJira.StoryPointFieldId]);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"discover did not write {pipeline.ConfigPath}, because run would refuse it: "
            + $"The configuration file {pipeline.ConfigPath} is not valid: jira.sprintFieldId and jira.storyPointFieldId name the same field.\n");
        await Assert.That(File.Exists(pipeline.ConfigPath)).IsFalse();
    }

    [Test]
    public async Task The_first_discover_writes_the_sprint_field_and_empty_lists_for_the_owner_to_fill()
    {
        await using var pipeline = new PipelineHarness();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["jira"]!["sprintFieldId"]!.GetValue<string>()).IsEqualTo("customfield_10020");
        await Assert.That(written["abandonedResolutionIds"]!.ToJsonString()).IsEqualTo("[]");
        await Assert.That(written["holidays"]!.ToJsonString()).IsEqualTo("[]");
        await Assert.That(Output(pipeline)).Contains("Before the first run, review the status roles and fill in abandonedResolutionIds and holidays.\n");
    }

    [Test]
    public async Task A_rerun_keeps_the_abandoned_resolutions_and_holidays_the_owner_listed()
    {
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config =>
        {
            config["abandonedResolutionIds"] = new JsonArray("300002");
            config["holidays"] = new JsonArray("2026-01-19", "2026-05-25");
        });

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover", "--time-zone", "Europe/London");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["timeZone"]!.GetValue<string>()).IsEqualTo("Europe/London");
        await Assert.That(written["abandonedResolutionIds"]!.ToJsonString()).IsEqualTo("""["300002"]""");
        await Assert.That(written["holidays"]!.ToJsonString()).IsEqualTo("""["2026-01-19","2026-05-25"]""");
    }

    [Test]
    public async Task An_abandoned_resolution_run_would_refuse_stops_a_rerun()
    {
        // A resolution's name where its ID belongs would match no issue.
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config => config["abandonedResolutionIds"] = new JsonArray("Fake Won't Do"));
        var edited = await File.ReadAllTextAsync(pipeline.ConfigPath);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover", "--time-zone", "Europe/London");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("run would refuse it").And.Contains("abandonedResolutionIds: every entry must be a Jira resolution ID");
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(edited);
    }

    [Test]
    public async Task A_rerun_keeps_the_order_of_the_status_roles()
    {
        // The order is board order and reports list statuses in it. Here it is not the order of the IDs.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.BoardColumns.Reverse();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover", "--time-zone", "Europe/London");

        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["timeZone"]!.GetValue<string>()).IsEqualTo("Europe/London");
        await Assert.That(string.Join(" ", written["statusRoles"]!.AsObject().Select(role => role.Key)))
            .IsEqualTo("600008 600009 600004 600005 600006 600007 600001 600002 600003");
    }

    [Test]
    [Arguments("--sprint-field", "customfield_10404", "--sprint-field: Jira's field list has no field 'customfield_10404'. Nothing was written.")]
    [Arguments("--story-point-field", "customfield_10404", "--story-point-field: Jira's field list has no field 'customfield_10404'. Nothing was written.")]
    [Arguments("--story-point-field", "customfield_10020", "--story-point-field: the field 'customfield_10020' does not hold a number, so it cannot hold story points. Nothing was written.")]
    public async Task A_field_the_owner_names_is_checked_against_jiras_field_list(string option, string fieldId, string message)
    {
        await using var pipeline = new PipelineHarness();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync([.. FirstDiscover, option, fieldId]);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(message + "\n");
        await Assert.That(File.Exists(pipeline.ConfigPath)).IsFalse();
    }

    [Test]
    public async Task A_story_point_field_in_the_file_that_jira_does_not_list_stops_a_rerun_and_leaves_the_file()
    {
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config =>
        {
            config["jira"]!["storyPointFieldId"] = "customfield_10404";
            config["timeZone"] = "Europe/London";
        });
        var edited = await File.ReadAllTextAsync(pipeline.ConfigPath);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"jira.storyPointFieldId in {pipeline.ConfigPath}: Jira's field list has no field 'customfield_10404'. Nothing was written.\n");
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(edited);
    }

    [Test]
    public async Task A_board_status_with_no_proposal_is_listed_and_left_out_of_the_file()
    {
        // One status is in a category that matches no role; another is on the board but not in the site's status list.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.BoardColumns.Clear();
        pipeline.Jira.BoardColumns.Add(new FakeColumn("Fake Only", ["600008", "600011", "600099"]));
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/rest/api/3/status", _ => true, StubHttpHandler.Json(
            """
            [
              { "id": "600008", "name": "Fake Done", "statusCategory": { "id": 3, "key": "done", "name": "Done" } },
              { "id": "600011", "name": "Fake Limbo", "statusCategory": { "id": 1, "key": "undefined", "name": "No Category" } }
            ]
            """));

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(Output(pipeline)).Contains("  600011  Fake Limbo  undefined  no role (discover proposes none; give it one in the file if issues use this status)\n");
        await Assert.That(Output(pipeline)).Contains("  600099  (not in Jira's status list)  -  no role (discover proposes none; give it one in the file if issues use this status)\n");
        var roles = (await ReadWrittenConfigAsync(pipeline))["statusRoles"]!.AsObject();
        await Assert.That(roles.Select(role => role.Key)).IsEquivalentTo(["600008"]);
    }

    [Test]
    public async Task A_board_without_projects_stops_discover()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.BoardProjectKeys.Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Jira lists no project for board 400001, so there is nothing to search. Check the board ID. Nothing was written.\n");
        await Assert.That(File.Exists(pipeline.ConfigPath)).IsFalse();
    }

    [Test]
    public async Task A_project_key_run_would_refuse_stops_discover()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.BoardProjectKeys.Add("fake-low");

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Jira lists a project of board 400001 whose key this tool cannot use: 'fake-low' is not a Jira project key "
            + "(an upper-case letter followed by upper-case letters, digits or underscores). Nothing was written.\n");
        await Assert.That(File.Exists(pipeline.ConfigPath)).IsFalse();
    }

    [Test]
    [Arguments("/rest/api/3/field", """{ "fields": [] }""", "field list", "InvalidOperationException")]
    [Arguments("/rest/api/3/field", """[ { "id": "customfield_10020", "schema": { "custom": "com.pyxis.greenhopper.jira:gh-sprint" } } ]""", "field list", "KeyNotFoundException")]
    [Arguments("/rest/api/3/resolution/search", """[ { "id": "300001", "name": "Fake Done" } ]""", "resolution list", "InvalidOperationException")]
    [Arguments("/rest/api/3/resolution/search", """{ "isLast": true, "values": [ { "id": "300001", "name": null } ] }""", "resolution list", "FormatException")]
    [Arguments("/rest/agile/1.0/board/400001/project", """{ "isLast": true, "values": [ { "id": "100001" } ] }""", "list of the board's projects", "KeyNotFoundException")]
    [Arguments("/rest/api/3/status", """[ { "id": "600001", "name": "Fake New" } ]""", "status list", "KeyNotFoundException")]
    public async Task An_answer_in_an_unknown_shape_stops_discover_plainly_and_writes_nothing(string path, string answer, string what, string kind)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Handler.Override(HttpMethod.Get, path, _ => true, StubHttpHandler.Json(answer));

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"Jira's {what} answered in a shape this tool does not understand ({kind}). Nothing was written.\n");
        await Assert.That(File.Exists(pipeline.ConfigPath)).IsFalse();
    }

    [Test]
    [Arguments(SecretName.JiraEmail, "'Jira email'", "jira-email")]
    [Arguments(SecretName.JiraApiToken, "'Jira API token'", "jira-api-token")]
    public async Task A_missing_jira_secret_stops_discover_before_any_call_and_says_how_to_set_it(SecretName missing, string expectedName, string argument)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Secrets.Remove(missing);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The secret {expectedName} is not set in the secret store. Run 'telltale secrets set {argument}'.\n");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    public async Task Discover_does_not_need_the_hashing_salt()
    {
        // It stores nothing about people, and the salt may not exist yet on the first day.
        await using var pipeline = new PipelineHarness();
        pipeline.Secrets.Remove(SecretName.HashingSalt);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
    }

    [Test]
    public async Task A_rerun_that_finds_nothing_new_leaves_the_file_and_any_backup_alone()
    {
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        var written = await File.ReadAllTextAsync(pipeline.ConfigPath);
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(written);
        await Assert.That(File.Exists(pipeline.ConfigPath + ".bak")).IsFalse();
        await Assert.That(Output(pipeline)).Contains($"{pipeline.ConfigPath} already says all of this; it was not rewritten.\n");
    }

    [Test]
    public async Task Discover_creates_the_directory_of_the_configuration_file()
    {
        // The production path is config/telltale.json, and on the first day there is no config directory.
        await using var pipeline = new PipelineHarness();
        var path = Path.Combine(pipeline.Directory, "config", "telltale.json");
        var services = new AppServices(pipeline.Jira.Handler, pipeline.Clock, new InMemorySecretStore(pipeline.Secrets), pipeline.Out, pipeline.Error,
            path, pipeline.DatabasePath, pipeline.ReportDirectory);

        var exit = await CliApp.RunAsync(FirstDiscover, services);

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(File.Exists(path)).IsTrue();
    }

    [Test]
    public async Task A_site_url_with_a_path_and_no_trailing_slash_keeps_its_path()
    {
        // Without the slash, https://host/tenant + rest/api would become https://host/rest/api.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Handler.On(HttpMethod.Get, "/tenant/rest/api/3/status", _ => true,
            _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "synthetic: the tenant path is not served"));
        string[] args = [.. FirstDiscover];
        args[Array.IndexOf(args, "--site-url") + 1] = "https://example.invalid/tenant";

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(args);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Jira.Handler.Requests.Select(request => request.Path)).IsEquivalentTo(["/tenant/rest/api/3/status"]);
    }

    [Test]
    public async Task A_first_discover_whose_file_run_would_refuse_writes_nothing_and_says_why()
    {
        // No status is in Jira's done category, so no role "done" is proposed, and run refuses a file without one.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/rest/api/3/status", _ => true, StubHttpHandler.Json(
            """[ { "id": "600004", "name": "Fake Development", "statusCategory": { "id": 4, "key": "indeterminate", "name": "In Progress" } } ]"""));

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"discover did not write {pipeline.ConfigPath}, because run would refuse it: "
            + $"The configuration file {pipeline.ConfigPath} is not valid: statusRoles: no status has the role 'done', so nothing could ever be delivered.\n");
        await Assert.That(File.Exists(pipeline.ConfigPath)).IsFalse();
    }

    [Test]
    [Arguments("\"finished\"", "'finished' is not a status role")]
    [Arguments("null", "statusRoles")]
    public async Task A_role_in_the_file_that_run_would_refuse_stops_a_rerun_and_is_neither_replaced_nor_copied(string role, string named)
    {
        // A role the owner wrote is the owner's, even a wrong one: discover says so and does not put its proposal in its place.
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        var edited = (await File.ReadAllTextAsync(pipeline.ConfigPath)).Replace("\"600004\": \"work\"", $"\"600004\": {role}", StringComparison.Ordinal);
        await File.WriteAllTextAsync(pipeline.ConfigPath, edited);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover", "--time-zone", "Europe/London");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("run would refuse it").And.Contains(named);
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(edited).And.Contains($"\"600004\": {role}");
        await Assert.That((await pipeline.ReadAllFilesAsync()).Keys).IsEquivalentTo(["telltale.json"]);
    }

    [Test]
    public async Task A_file_that_differs_only_in_layout_and_comments_is_not_rewritten()
    {
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        var compact = "// the owner's note\n" + (await ReadWrittenConfigAsync(pipeline)).ToJsonString();
        await File.WriteAllTextAsync(pipeline.ConfigPath, compact);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(compact);
        await Assert.That((await pipeline.ReadAllFilesAsync()).Keys).IsEquivalentTo(["telltale.json"]);
    }

    [Test]
    public async Task A_temporary_file_left_by_a_killed_discover_is_replaced_and_none_is_left_behind()
    {
        await using var pipeline = new PipelineHarness();
        await File.WriteAllTextAsync(pipeline.ConfigPath + ".tmp", "half a fi");

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That((await pipeline.ReadAllFilesAsync()).Keys).IsEquivalentTo(["telltale.json"]);
        await Assert.That((await ReadWrittenConfigAsync(pipeline))["timeZone"]!.GetValue<string>()).IsEqualTo(ZoneId);
    }

    [Test]
    [Arguments("proxy authentication")]
    [Arguments("timeout")]
    [Arguments("web page")]
    public async Task A_first_call_that_never_gets_an_answer_from_jira_names_the_proxy_and_the_root_certificate(string how)
    {
        // A proxy can ask for its own sign-in, swallow the request, or serve a block page. None of these looks like TLS.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/rest/api/3/status", _ => true, how switch
        {
            "proxy authentication" => StubHttpHandler.Json("""{ "message": "synthetic proxy challenge" }""", HttpStatusCode.ProxyAuthenticationRequired),
            "timeout" => _ => throw new TaskCanceledException("synthetic timeout", new TimeoutException()),
            _ => _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>synthetic block page</html>") },
        });

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(FirstCallFailed + how switch
        {
            "proxy authentication" => "The proxy asked for authentication (407) on GET rest/api/3/status, so the request never reached Jira. "
                + "Check the system proxy settings and that this account may use the proxy.\n",
            "timeout" => "Jira did not answer GET rest/api/3/status in time.\n",
            _ => "The answer to GET rest/api/3/status was not JSON. A proxy or sign-in page may have been served in place of Jira's answer.\n",
        });
        await Assert.That(File.Exists(pipeline.ConfigPath)).IsFalse();
    }

    [Test]
    [Arguments(HttpStatusCode.Unauthorized, "401 (Unauthorized)")]
    [Arguments(HttpStatusCode.Forbidden, "403 (Forbidden)")]
    public async Task A_first_call_jira_refuses_names_the_jira_email_and_api_token_and_how_to_set_them(HttpStatusCode status, string shown)
    {
        // Jira answered, so the proxy and the certificate are fine. The answer's text is not shown.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/rest/api/3/status", _ => true,
            StubHttpHandler.Json("""{ "errorMessages": ["synthetic refusal quoting Fake Person One"] }""", status));

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Jira refused discover's first call. The likely cause is the Jira email or the API token in the secret store. "
            + "Run 'telltale secrets set jira-email'. Run 'telltale secrets set jira-api-token'. "
            + $"The failure was: Jira answered {shown} to GET rest/api/3/status.\n");
        await Assert.That(pipeline.Jira.Handler.Requests.Select(request => request.Path)).IsEquivalentTo(["/rest/api/3/status"]);
        await Assert.That(File.Exists(pipeline.ConfigPath)).IsFalse();
    }

    [Test]
    public async Task A_first_call_jira_answers_with_another_error_is_reported_without_a_guess()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/rest/api/3/status", _ => true,
            StubHttpHandler.Json("""{ "message": "synthetic outage" }""", HttpStatusCode.InternalServerError));

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("Jira answered 500 (InternalServerError) to GET rest/api/3/status.\n");
    }

    [Test]
    public async Task A_failure_after_the_first_call_is_not_blamed_on_the_proxy()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/rest/api/3/field", _ => true,
            StubHttpHandler.Json("""{ "message": "synthetic refusal" }""", HttpStatusCode.Forbidden));

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("Jira answered 403 (Forbidden) to GET rest/api/3/field.\n");
    }

    [Test]
    [Arguments("jira", "siteUrl", "\"http://example.invalid/\"", "jira.siteUrl in {0} must be an absolute https:// address.")]
    [Arguments("jira", "siteUrl", "5", "jira.siteUrl in {0} must be text.")]
    [Arguments("jira", "boardId", "\"four\"", "jira.boardId in {0} must be a whole number.")]
    [Arguments("jira", "boardId", "-1", "jira.boardId in {0} must be a positive number.")]
    [Arguments("jira", "backfillStart", "\"last year\"", "jira.backfillStart in {0} must be a date written as yyyy-MM-dd.")]
    [Arguments("", "timeZone", "\"Fake/Nowhere\"", "timeZone in {0}: 'Fake/Nowhere' is not a known IANA time zone ID.")]
    [Arguments("", "timeZone", "[]", "timeZone in {0} must be text.")]
    public async Task A_bad_value_in_the_file_is_reported_as_the_files_and_not_as_an_option(string section, string setting, string json, string message)
    {
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        var requestsBefore = pipeline.Jira.Handler.Requests.Count;
        await EditWrittenConfigAsync(pipeline, config => (section.Length == 0 ? config : config[section]!)[setting] = JsonNode.Parse(json));

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(string.Format(CultureInfo.InvariantCulture, message, pipeline.ConfigPath) + "\n");
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(requestsBefore);
    }

    [Test]
    public async Task A_paged_list_that_ignores_the_offset_ends_when_a_page_brings_nothing_new()
    {
        // Jira answers every request with the same page and never says it is the last. After three answers the stub fails the test.
        await using var pipeline = new PipelineHarness();
        var samePage = StubHttpHandler.Json("""{ "values": [ { "id": "300001", "name": "Fake Done" }, { "id": "300002", "name": "Fake Won't Do" } ] }""");
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/rest/api/3/resolution/search", _ => true,
            samePage, samePage, samePage, _ => throw new InvalidOperationException("synthetic: discover keeps asking for the same page"));

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(Output(pipeline)).Contains("Resolutions (ID, name):\n  300001  Fake Done\n  300002  Fake Won't Do\nSprint field");
    }

    [Test]
    public async Task A_paged_list_that_gives_its_total_is_not_asked_for_a_page_past_it()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/rest/api/3/resolution/search", _ => true,
            StubHttpHandler.Json("""{ "startAt": 0, "total": 2, "values": [ { "id": "300001", "name": "Fake Done" }, { "id": "300002", "name": "Fake Won't Do" } ] }"""),
            _ => throw new InvalidOperationException("synthetic: discover asked for a page past the total"));

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Jira.Handler.Requests.Count(request => request.Path == "/rest/api/3/resolution/search")).IsEqualTo(1);
    }

    private static string Output(PipelineHarness pipeline) => pipeline.Out.ToString().ReplaceLineEndings("\n");

    /// <summary>Changes the file on disk as the owner would in an editor.</summary>
    private static async Task EditWrittenConfigAsync(PipelineHarness pipeline, Action<JsonNode> edit)
    {
        var config = await ReadWrittenConfigAsync(pipeline);
        edit(config);
        await File.WriteAllTextAsync(pipeline.ConfigPath, config.ToJsonString());
    }

    private static async Task<JsonNode> ReadWrittenConfigAsync(PipelineHarness pipeline) =>
        JsonNode.Parse(await File.ReadAllTextAsync(pipeline.ConfigPath))!;
}
