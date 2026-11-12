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

    [Test]
    public async Task Run_accepts_the_file_discover_wrote_without_any_edit()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).AssignedTo(People.One)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 15), Workflow.Done);

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
            + "  600007  Fake Blocked  indeterminate  work (proposed)\n"
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
            $"Could not reach Jira (GET rest/api/3/status): {failure}. "
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
    public async Task A_rerun_keeps_the_roles_the_owner_edited_and_says_where_jiras_category_differs()
    {
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config =>
        {
            config["statusRoles"]![Workflow.Blocked.Id] = "blocked";
            config["statusRoles"]![Workflow.Committed.Id] = "work";
        });
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var roles = (await ReadWrittenConfigAsync(pipeline))["statusRoles"]!;
        await Assert.That(roles[Workflow.Blocked.Id]!.GetValue<string>()).IsEqualTo("blocked");
        await Assert.That(roles[Workflow.Committed.Id]!.GetValue<string>()).IsEqualTo("work");
        await Assert.That(roles[Workflow.Done.Id]!.GetValue<string>()).IsEqualTo("done");
        await Assert.That(Output(pipeline)).Contains("  600007  Fake Blocked  indeterminate  blocked (from the file; Jira's category proposes work)\n");
        await Assert.That(Output(pipeline)).Contains("  600003  Fake Committed  new  work (from the file; Jira's category proposes not started)\n");
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
    public async Task A_rerun_adds_a_status_new_in_jira_and_keeps_the_role_of_one_jira_no_longer_lists()
    {
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config => config["statusRoles"]!["600077"] = "blocked");
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/rest/api/3/status", _ => true, StubHttpHandler.Json(
            """
            [
              { "id": "600008", "name": "Fake Done", "statusCategory": { "id": 3, "key": "done", "name": "Done" } },
              { "id": "600010", "name": "Fake Verified", "statusCategory": { "id": 3, "key": "done", "name": "Done" } }
            ]
            """));
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover");

        await Assert.That(exit).IsEqualTo(0);
        var roles = (await ReadWrittenConfigAsync(pipeline))["statusRoles"]!;
        await Assert.That(roles["600010"]!.GetValue<string>()).IsEqualTo("done");
        await Assert.That(roles["600077"]!.GetValue<string>()).IsEqualTo("blocked");
        await Assert.That(roles[Workflow.Development.Id]!.GetValue<string>()).IsEqualTo("work");
        await Assert.That(Output(pipeline)).Contains("  600010  Fake Verified  done  done (proposed)\n");
        await Assert.That(Output(pipeline)).Contains("  600077  (not in Jira's status list)  blocked (from the file)\n");
    }

    [Test]
    public async Task A_rerun_keeps_settings_discover_does_not_write()
    {
        // Settings the owner adds by hand (the abandoned resolutions, holidays, the baseline) are not discover's to drop.
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config =>
        {
            config["fakeOwnerList"] = new JsonArray("300002");
            config["jira"]!["fakeOwnerSetting"] = "kept";
        });

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover");

        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["fakeOwnerList"]!.ToJsonString()).IsEqualTo("""["300002"]""");
        await Assert.That(written["jira"]!["fakeOwnerSetting"]!.GetValue<string>()).IsEqualTo("kept");
    }

    [Test]
    public async Task A_rerun_saves_the_previous_file_beside_the_new_one_and_says_so()
    {
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        var edited = (await File.ReadAllTextAsync(pipeline.ConfigPath)).Replace("{", "{ // the owner's note", StringComparison.Ordinal);
        await File.WriteAllTextAsync(pipeline.ConfigPath, edited);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath + ".bak")).IsEqualTo(edited);
        await Assert.That(Output(pipeline)).Contains($"The previous file is kept as {pipeline.ConfigPath}.bak");
    }

    [Test]
    public async Task A_rerun_says_which_discovered_setting_changed()
    {
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);
        pipeline.Jira.BoardProjectKeys.Add("FAKE2");

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(Output(pipeline)).Contains("""Changed jira.projectKeys: was ["FAKE"], now ["FAKE","FAKE2"]""" + "\n");
        await Assert.That(Output(pipeline)).DoesNotContain("Changed jira.siteUrl");
        await Assert.That(Output(pipeline)).DoesNotContain("Changed jira.boardId");
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
    public async Task A_field_list_without_a_sprint_field_stops_discover()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Fields.RemoveAll(field => field.Id == FakeJira.SprintFieldId);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Jira's field list has no Sprint field. Check that the site has Jira Software and that this account may see the field. Nothing was written.\n");
        await Assert.That(File.Exists(pipeline.ConfigPath)).IsFalse();
    }

    [Test]
    public async Task A_status_in_a_category_with_no_matching_role_is_listed_and_left_out_of_the_file()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/rest/api/3/status", _ => true, StubHttpHandler.Json(
            """
            [
              { "id": "600008", "name": "Fake Done", "statusCategory": { "id": 3, "key": "done", "name": "Done" } },
              { "id": "600011", "name": "Fake Limbo", "statusCategory": { "id": 1, "key": "undefined", "name": "No Category" } }
            ]
            """));

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(Output(pipeline)).Contains("  600011  Fake Limbo  undefined  no role (Jira's category proposes none; give it one in the file if issues use this status)\n");
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
    [Arguments(SecretName.JiraEmail, "'Jira email'")]
    [Arguments(SecretName.JiraApiToken, "'Jira API token'")]
    public async Task A_missing_jira_secret_stops_discover_before_any_call_and_is_named(SecretName missing, string expectedName)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Secrets.Remove(missing);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo($"The secret {expectedName} is not set in the secret store.\n");
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
        pipeline.Jira.Handler.On(HttpMethod.Get, "/tenant/rest/api/3/status",
            _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "synthetic: the tenant path is not served"));
        string[] args = [.. FirstDiscover];
        args[Array.IndexOf(args, "--site-url") + 1] = "https://example.invalid/tenant";

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(args);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Jira.Handler.Requests.Select(request => request.Path)).IsEquivalentTo(["/tenant/rest/api/3/status"]);
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
