using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Telltale.Collectors.Jira;
using Telltale.Core;
using Telltale.Core.Configuration;
using Telltale.Core.Workflow;

namespace Telltale.Cli.Commands;

/// <summary>
/// <c>discover</c>: reads a Jira site and writes the configuration file, so the owner does not look up IDs by
/// hand. What it proposes is meant to be reviewed and edited, so a rerun starts from the file as it is: what
/// the owner wrote stays (roles, removed statuses, project keys, the story point field), differences from
/// Jira are printed, and the previous file is kept under a dated name. It never writes a file the run
/// would refuse.
/// </summary>
internal static class DiscoverCommand
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // The file is read as leniently as the run reads it (comments, trailing commas), and a setting written
    // twice is refused for the same reason: nobody could tell which value is in force.
    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        AllowDuplicateProperties = false,
    };

    public static Command Create(AppServices services)
    {
        var siteUrl = new Option<string?>("--site-url") { Description = "The Jira site, e.g. https://example.atlassian.net/. Needed the first time." };
        var board = new Option<long?>("--board") { Description = "The ID of the team's Scrum board. Needed the first time." };
        var timeZone = new Option<string?>("--time-zone") { Description = "The team's time zone as an IANA ID, e.g. America/Chicago. Needed the first time." };
        var backfillStart = new Option<string?>("--backfill-start") { Description = "The backfill fetches issues updated on or after this date (yyyy-MM-dd). Needed the first time." };
        var storyPointField = new Option<string?>("--story-point-field") { Description = "The ID of the board's story point field, e.g. customfield_10016. Needed when Jira lists none or several." };
        var sprintField = new Option<string?>("--sprint-field") { Description = "The ID of the Sprint field, e.g. customfield_10020. Needed when Jira lists none or several." };

        var command = new Command("discover", "Read the Jira site and write the configuration file. A rerun keeps what the file already says.")
        {
            siteUrl, board, timeZone, backfillStart, storyPointField, sprintField,
        };
        command.SetAction((parse, cancellationToken) => CliApp.GuardAsync(services, () => DiscoverAsync(
            services,
            new Given(parse.GetValue(siteUrl), parse.GetValue(board), parse.GetValue(timeZone), parse.GetValue(backfillStart), parse.GetValue(storyPointField), parse.GetValue(sprintField)),
            cancellationToken)));

        return command;
    }

    /// <summary>What the command line gave. Null means "take it from the file".</summary>
    private sealed record Given(string? SiteUrl, long? Board, string? TimeZone, string? BackfillStart, string? StoryPointField, string? SprintField);

    private static async Task DiscoverAsync(AppServices services, Given given, CancellationToken cancellationToken)
    {
        var path = services.ConfigPath;
        var previous = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;

        // Everything that can be refused is refused before the first call to Jira and before any file is written.
        var root = previous is null ? [] : ReadExisting(previous, path);
        var before = root.DeepClone();
        var jira = Section(root, "jira", path);
        var statusRoles = Section(root, "statusRoles", path);
        RequireRunWouldAcceptRolesAndProjectKeys(jira, statusRoles, path);

        // The roles are proposed by the discover that finds none in the file, whether or not a file was there.
        var fileHadNoRoles = statusRoles.Count == 0;

        // Each of these settings comes from its option, or else from the file, and whichever is used is checked
        // once, by run's rule for it. An option may replace a value in the file that run would refuse: that is
        // how discover repairs a file. Such an old value could be a secret pasted into the wrong place, so it
        // is never shown, and the old file is then not kept as a backup.
        var changes = new List<string>();
        var replacedUnshown = new List<string>();

        string? Choose(string? given, string option, JsonObject section, string name, string setting, Func<string, bool, string?> problemOf)
        {
            var inFile = section[name];
            var fileText = TextOf(inFile);
            var fileProblem = inFile is null ? null : fileText is null ? " must be text." : problemOf(fileText, false);
            if (given is null)
            {
                return fileProblem is null ? fileText : throw new UserFacingException($"{setting} in {path}{fileProblem}");
            }

            if (problemOf(given, true) is { } optionProblem)
            {
                throw new UserFacingException(option + optionProblem);
            }

            if (fileProblem is not null)
            {
                ReplaceUnshown(section, name, given, setting);
            }

            return given;
        }

        void ReplaceUnshown(JsonObject section, string name, JsonNode value, string setting)
        {
            section[name] = value;
            replacedUnshown.Add(setting);
            changes.Add(setting == "jira.siteUrl" ? "Changed jira.siteUrl (the old address is not shown)." : $"Changed {setting} (the old value is not shown).");
        }

        long? ChooseBoard()
        {
            var inFile = jira["boardId"];
            long? fileNumber = inFile is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;
            var fileProblem = inFile is null ? null : fileNumber is null ? " must be a whole number." : BoardProblem(fileNumber.Value);
            if (given.Board is not { } board)
            {
                return fileProblem is null ? fileNumber : throw new UserFacingException($"jira.boardId in {path}{fileProblem}");
            }

            if (BoardProblem(board) is { } optionProblem)
            {
                throw new UserFacingException("--board" + optionProblem);
            }

            if (fileProblem is not null)
            {
                ReplaceUnshown(jira, "boardId", board, "jira.boardId");
            }

            return board;
        }

        var siteText = Choose(given.SiteUrl, "--site-url", jira, "siteUrl", "jira.siteUrl", SiteProblem);
        var boardId = ChooseBoard();
        var timeZone = Choose(given.TimeZone, "--time-zone", root, "timeZone", "timeZone", TimeZoneProblem);
        var backfillStart = Choose(given.BackfillStart, "--backfill-start", jira, "backfillStart", "jira.backfillStart", DateProblem);
        var sprintInFile = Choose(given.SprintField, SprintField.Option, jira, SprintField.Setting, $"jira.{SprintField.Setting}", FieldIdProblem("customfield_10020"));
        var storyPointsInFile = Choose(given.StoryPointField, StoryPointField.Option, jira, StoryPointField.Setting, $"jira.{StoryPointField.Setting}", FieldIdProblem("customfield_10016"));
        RequireAll(path, ("--site-url", siteText is null), ("--board", boardId is null), ("--time-zone", timeZone is null), ("--backfill-start", backfillStart is null));

        var site = WithTrailingSlash(new Uri(siteText!));
        if (replacedUnshown.Contains("jira.siteUrl"))
        {
            // The change is already noted. The address is written as it will be, so it is not noted twice.
            jira["siteUrl"] = site.AbsoluteUri;
        }

        var facts = await new JiraDiscovery(services.HttpHandler, site, boardId!.Value, services.Secrets, services.Clock, services.Error).ReadAsync(cancellationToken);

        // Whatever would make the file useless stops the command here, before anything is printed or written.
        var sprint = ChooseField(SprintField, given.SprintField, sprintInFile, facts.SprintFields, facts, path);
        var storyPoints = ChooseField(StoryPointField, given.StoryPointField, storyPointsInFile, facts.StoryPointFields, facts, path);
        var (projectKeys, projectKeySource) = ChooseProjectKeys(jira["projectKeys"], facts.ProjectKeys, boardId.Value);

        var output = services.Out;
        await ListStatusesAsync(output, facts, statusRoles);

        await output.WriteLineAsync("Resolutions (ID, name):");
        foreach (var resolution in facts.Resolutions)
        {
            await output.WriteLineAsync($"  {resolution.Id}  {resolution.Name}");
        }

        await output.WriteLineAsync($"Sprint field: {sprint.Id} ({sprint.Source})");
        await output.WriteLineAsync($"Story point field: {storyPoints.Id} ({storyPoints.Source})");
        await output.WriteLineAsync($"Project keys: {projectKeySource}");

        Set(jira, "siteUrl", site.AbsoluteUri, "jira.siteUrl", changes, showValues: false);
        Set(jira, "boardId", boardId.Value, "jira.boardId", changes);
        Set(jira, "projectKeys", projectKeys, "jira.projectKeys", changes);
        Set(jira, "sprintFieldId", sprint.Id, "jira.sprintFieldId", changes);
        Set(jira, "storyPointFieldId", storyPoints.Id, "jira.storyPointFieldId", changes);
        Set(jira, "backfillStart", backfillStart!, "jira.backfillStart", changes);

        // Jira cannot say which resolutions mean abandoned or which days are holidays. Both lists are the
        // owner's: discover starts them empty and never touches them again.
        root["abandonedResolutionIds"] ??= new JsonArray();
        Set(root, "timeZone", timeZone!, "timeZone", changes);
        root["holidays"] ??= new JsonArray();

        foreach (var change in changes)
        {
            await output.WriteLineAsync(change);
        }

        var text = root.ToJsonString(Indented) + Environment.NewLine;
        var hasDoneRole = await RequireRunWouldAcceptAsync(text, path, mayLackDoneRole: fileHadNoRoles, cancellationToken);

        if (JsonNode.DeepEquals(root, before))
        {
            // Only layout or comments differ, if anything. Rewriting would drop the comments for nothing.
            await output.WriteLineAsync($"{path} already says all of this; it was not rewritten.");
            return;
        }

        // An old file that held a value run would refuse is not kept: the copy would hold that value.
        var backup = await ReplaceFileAsync(path, text, keepPrevious: previous is not null && replacedUnshown.Count == 0, services.Clock, cancellationToken);
        if (previous is null)
        {
            await output.WriteLineAsync($"Wrote {path}. Before the first run, review the status roles and fill in abandonedResolutionIds and holidays.");
        }
        else if (backup is null)
        {
            var settings = replacedUnshown.Count == 1 ? replacedUnshown[0] : $"{string.Join(", ", replacedUnshown.Take(replacedUnshown.Count - 1))} and {replacedUnshown[^1]}";
            var refused = replacedUnshown.Count == 1 ? "is a value" : "are values";
            await output.WriteLineAsync($"Wrote {path}. The previous file was not kept: its {settings} {refused} run would refuse, and such a value could be a secret.");
        }
        else
        {
            await output.WriteLineAsync($"Wrote {path}. The previous file is kept as {backup}.");
            if (HasComments(previous))
            {
                await output.WriteLineAsync($"The previous file had comments, and the new one has none: they are only in {backup}.");
            }
        }

        if (!hasDoneRole)
        {
            await output.WriteLineAsync("No status has the role 'done' yet, so run will refuse this file: nothing could ever be delivered. Give the role done to at least one status in statusRoles.");
        }
    }

    /// <summary>
    /// Lists the board's statuses in column order with their roles (owner decision D29). On the first discover
    /// each gets the proposed role. Once the file has roles, the file is the owner's: a role in it stays, and a
    /// board status it leaves out is reported and not added, because the owner may have removed it.
    /// </summary>
    private static async Task ListStatusesAsync(TextWriter output, JiraSiteFacts facts, JsonObject statusRoles)
    {
        var known = facts.Statuses.ToDictionary(status => status.Id, StringComparer.Ordinal);
        var fileHasRoles = statusRoles.Count > 0;

        string Describe(string statusId) => known.TryGetValue(statusId, out var status)
            ? $"  {status.Id}  {status.Name}  {status.CategoryKey}"
            : $"  {statusId}  (not in Jira's status list)  -";

        await output.WriteLineAsync("Statuses (ID, name, Jira category, role):");
        foreach (var statusId in facts.BoardStatusIds)
        {
            var proposal = known.TryGetValue(statusId, out var status) ? ProposeRole(status) : null;

            // Every role in the file is one of the four by now (RequireRunWouldAcceptRolesAndProjectKeys).
            if (statusRoles.TryGetPropertyValue(statusId, out var inFile))
            {
                var role = RoleText(inFile);
                var differs = proposal is not null && proposal != role ? $"; discover proposes {proposal}" : "";
                await output.WriteLineAsync($"{Describe(statusId)}  {role} (from the file{differs})");
            }
            else if (fileHasRoles)
            {
                await output.WriteLineAsync($"{Describe(statusId)}  left out of the file (the board has this status; give it a role in the file if issues use it)");
            }
            else if (proposal is null)
            {
                // The file takes one of the four roles or nothing. The run stops only if an issue has been in this status.
                await output.WriteLineAsync($"{Describe(statusId)}  no role (discover proposes none; give it one in the file if issues use this status)");
            }
            else
            {
                statusRoles[statusId] = proposal;
                await output.WriteLineAsync($"{Describe(statusId)}  {proposal} (proposed)");
            }
        }

        var onBoard = facts.BoardStatusIds.ToHashSet(StringComparer.Ordinal);
        foreach (var (statusId, inFile) in statusRoles.Where(role => !onBoard.Contains(role.Key)))
        {
            await output.WriteLineAsync($"{Describe(statusId)}  {RoleText(inFile)} (from the file; not on the board)");
        }
    }

    private static string RoleText(JsonNode? inFile) => TextOf(inFile) ?? throw new InvalidOperationException("A role in the file was not checked before it was listed.");

    /// <summary>
    /// The role proposed for a status. A name containing "block" proposes blocked (owner decision D30), because
    /// Jira has no category for it; otherwise Jira's status category decides. Null when neither says anything.
    /// </summary>
    private static string? ProposeRole(DiscoveredStatus status) =>
        status.Name.Contains("block", StringComparison.OrdinalIgnoreCase) ? "blocked" : status.CategoryKey switch
        {
            "new" => "not started",
            "indeterminate" => "work",
            "done" => "done",
            _ => null,
        };

    /// <summary>What differs between the two fields discover has to find.</summary>
    /// <param name="Setting">The setting's name inside the <c>jira</c> section.</param>
    /// <param name="Option">The option that names the field.</param>
    /// <param name="Noun">How messages call the field.</param>
    /// <param name="NoneListed">What to say when Jira's field list shows none.</param>
    /// <param name="HoldsNumber">Whether the field must hold a number.</param>
    private sealed record FieldKind(string Setting, string Option, string Noun, string NoneListed, bool HoldsNumber);

    private static readonly FieldKind SprintField = new(
        "sprintFieldId", "--sprint-field", "Sprint field",
        "Jira's field list has no Sprint field. Check that the site has Jira Software and that this account may see the field, or give the field's ID with --sprint-field.",
        HoldsNumber: false);

    private static readonly FieldKind StoryPointField = new(
        "storyPointFieldId", "--story-point-field", "story point field",
        "Jira's field list has no field this tool recognises as story points. Give the field's ID with --story-point-field.",
        HoldsNumber: true);

    /// <summary>
    /// A field's ID and where it came from. The owner's word wins: the option, then the file. Jira's list
    /// decides only when it names exactly one field; a site often has two story point fields, and the board
    /// uses one. A field the owner names must be in Jira's field list, and hold a number where the kind says so.
    /// <paramref name="inFile"/> is the file's value, already checked to be a custom field ID.
    /// </summary>
    private static (string Id, string Source) ChooseField(FieldKind kind, string? given, string? inFile, IReadOnlyList<DiscoveredField> listed, JiraSiteFacts facts, string path)
    {
        var names = string.Join(", ", listed.Select(field => $"{field.Id} ({field.Name})"));
        var settingInFile = $"jira.{kind.Setting} in {path}";

        if ((given ?? inFile) is { } named)
        {
            var from = given is not null ? kind.Option : settingInFile;
            var field = RequireListed(named, from, facts);
            if (kind.HoldsNumber && field.ValueType != "number")
            {
                throw new UserFacingException($"{from}: the field '{named}' does not hold a number, so it cannot hold story points. Nothing was written.");
            }

            return given is not null
                ? (named, $"given with {kind.Option}")
                : (named, listed.Count == 0 || listed.Any(field => field.Id == named) ? "from the file" : $"from the file; Jira lists {names}");
        }

        return listed.Count switch
        {
            1 => (listed[0].Id, listed[0].Name),
            0 => throw new UserFacingException($"{kind.NoneListed} Nothing was written."),
            _ => throw new UserFacingException($"Jira lists more than one {kind.Noun}: {names}. Say which one the board uses with {kind.Option}. Nothing was written."),
        };
    }

    private static DiscoveredField RequireListed(string fieldId, string from, JiraSiteFacts facts) =>
        facts.Fields.FirstOrDefault(field => field.Id == fieldId)
            ?? throw new UserFacingException($"{from}: Jira's field list has no field '{fieldId}'. Nothing was written.");

    /// <summary>
    /// The project keys to write and what to print about them. Keys already in the file are the owner's and
    /// stay; what Jira lists is printed beside them when it differs. Without any, Jira's list is used, and
    /// it must be one the run accepts.
    /// </summary>
    private static (JsonNode Keys, string Source) ChooseProjectKeys(JsonNode? inFile, IReadOnlyList<string> listed, long boardId)
    {
        if (inFile is not null)
        {
            // The keys are project keys by now (RequireRunWouldAcceptRolesAndProjectKeys), so they can be shown.
            var kept = inFile.AsArray().Select(key => TextOf(key)!).ToList();
            var shown = string.Join(", ", kept);
            return (inFile.DeepClone(), kept.ToHashSet(StringComparer.Ordinal).SetEquals(listed) ? $"{shown} (from the file)" : $"{shown} (from the file; Jira lists {string.Join(", ", listed)})");
        }

        if (listed.Count == 0)
        {
            throw new UserFacingException($"Jira lists no project for board {boardId.ToString(CultureInfo.InvariantCulture)}, so there is nothing to search. Check the board ID. Nothing was written.");
        }

        // This key is Jira's answer, not the file's, and the owner needs to know which project it is.
        if (listed.FirstOrDefault(key => !JiraProjectKey.IsValid(key)) is { } unusable)
        {
            throw new UserFacingException(
                $"Jira lists a project of board {boardId.ToString(CultureInfo.InvariantCulture)} whose key this tool cannot use: '{unusable}' is not a Jira project key "
                + "(an upper-case letter followed by upper-case letters, digits or underscores). Nothing was written.");
        }

        return (new JsonArray([.. listed.Select(key => (JsonNode)key)]), string.Join(", ", listed));
    }

    /// <summary>
    /// Puts the text about to be written through the checks the run makes when it loads the file, so discover
    /// never leaves a file the run refuses: a setting the tool does not know (a pasted secret, for one), a
    /// role outside the four, no done role. The run's message names the setting and never its value.
    /// One file the run refuses is written all the same (owner decision D40), when <paramref name="mayLackDoneRole"/>
    /// says so: the file had no roles before this discover, and the board has no status discover can propose
    /// as done. The owner has to choose one, and needs the file to do it in. Returns whether a status has the
    /// done role: false in that case alone, so the caller can say so.
    /// </summary>
    private static async Task<bool> RequireRunWouldAcceptAsync(string text, string path, bool mayLackDoneRole, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            var config = await ConfigFile.ReadAsync(stream, path, cancellationToken);
            var roles = PipelineSteps.ReadStatusRoles(config, path, requireDone: !mayLackDoneRole);
            PipelineSteps.ReadAbandonedResolutionIds(config, path);
            PipelineSteps.ReadTimeZone(config, path);
            return roles.HasDone;
        }
        catch (UserFacingException exception)
        {
            throw new UserFacingException($"discover did not write {path}, because run would refuse it: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Replaces the file in one step: the text goes to a temporary file beside it, which then takes the
    /// file's name, so a cancelled or killed command leaves the old file whole. The old file is first copied
    /// to a dated name that is never reused. Returns the copy's path, or null when there was no old file.
    /// </summary>
    private static async Task<string?> ReplaceFileAsync(string path, string text, bool keepPrevious, TimeProvider clock, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllTextAsync(temporary, text, cancellationToken);

            var backup = keepPrevious ? KeepPrevious(path, clock) : null;
            File.Move(temporary, path, overwrite: true);
            return backup;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static string KeepPrevious(string path, TimeProvider clock)
    {
        var stamp = clock.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        for (var attempt = 1; ; attempt++)
        {
            var backup = attempt == 1 ? $"{path}.{stamp}.bak" : $"{path}.{stamp}-{attempt.ToString(CultureInfo.InvariantCulture)}.bak";
            if (!File.Exists(backup))
            {
                File.Copy(path, backup, overwrite: false);
                return backup;
            }
        }
    }

    private static bool HasComments(string text)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Allow, AllowTrailingCommas = true });
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.Comment)
            {
                return true;
            }
        }

        return false;
    }

    private static JsonObject ReadExisting(string text, string path)
    {
        try
        {
            return JsonNode.Parse(text, documentOptions: Lenient) as JsonObject ?? throw Unreadable(path, "it is not a JSON object");
        }
        catch (JsonException exception)
        {
            throw Unreadable(path, exception.Message, exception);
        }
    }

    /// <summary>A section of the file, added when the file does not have it yet.</summary>
    private static JsonObject Section(JsonObject root, string name, string path)
    {
        if (root[name] is null)
        {
            root[name] = new JsonObject();
        }

        return root[name] as JsonObject ?? throw Unreadable(path, $"{name} is not a JSON object");
    }

    private static UserFacingException Unreadable(string path, string why, Exception? inner = null)
    {
        var message = $"The configuration file {path} cannot be read ({why}), so it was left as it is. Correct it or move it away, then run discover again.";
        return inner is null ? new UserFacingException(message) : new UserFacingException(message, inner);
    }

    /// <summary>
    /// The settings of the file no option can replace, and discover shows: the status roles and the project
    /// keys. They are checked by the rule the run applies, before Jira is asked anything and before anything is
    /// printed or copied. The messages name the setting, and for a role the status ID, never the value, which
    /// could be a secret pasted into the wrong place. The settings discover only carries over (the abandoned
    /// resolutions, the holidays, anything the tool does not know) are never shown, and the check of the text
    /// about to be written refuses them.
    /// </summary>
    private static void RequireRunWouldAcceptRolesAndProjectKeys(JsonObject jira, JsonObject statusRoles, string path)
    {
        // The IDs first: a role's message shows its status ID.
        if (statusRoles.Any(role => !IsStatusId(role.Key)))
        {
            throw new UserFacingException($"statusRoles in {path}: every status ID must be a Jira status ID (digits only), and one is not. Correct it in the file, then run discover again.");
        }

        foreach (var (statusId, role) in statusRoles)
        {
            if (TextOf(role) is not { } label || !StatusRole.TryParse(label, out _))
            {
                throw new UserFacingException(
                    $"statusRoles in {path}: the role of status {statusId} is not one of: not started, work, blocked, done. Correct it in the file, then run discover again.");
            }
        }

        if (jira["projectKeys"] is { } projectKeys && !(projectKeys is JsonArray keys && keys.All(key => JiraProjectKey.IsValid(TextOf(key)))))
        {
            throw new UserFacingException(
                $"jira.projectKeys in {path} must be a list of Jira project keys (an upper-case letter followed by upper-case letters, digits or underscores), and it is not. "
                + "Correct it in the file, then run discover again.");
        }
    }

    private static bool IsStatusId(string statusId) => statusId.Length > 0 && statusId.All(char.IsAsciiDigit);

    // What run would refuse in one value, as the end of a sentence that starts with where the value came from
    // (the option, or the setting and the file), or null. The second argument says the value came from an
    // option. Only then may a message repeat it: the command line is the owner's own, the file may hold a secret.

    /// <summary>The rules the run applies to <c>jira.siteUrl</c>: https only, because the Jira email and API token travel with every request, and no secret in the address.</summary>
    private static string? SiteProblem(string text, bool fromOption) =>
        !Uri.TryCreate(text, UriKind.Absolute, out var site) || site.Scheme != Uri.UriSchemeHttps ? " must be an absolute https:// address."
        : site.UserInfo.Length > 0 ? " must not carry a user name or password."
        : null;

    private static string? BoardProblem(long boardId) => boardId <= 0 ? " must be a positive number." : null;

    private static string? TimeZoneProblem(string timeZone, bool fromOption) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _) ? null
        : fromOption ? $": '{timeZone}' is not a known IANA time zone ID."
        : " is not a known IANA time zone ID.";

    private static string? DateProblem(string date, bool fromOption) =>
        DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? null : " must be a date written as yyyy-MM-dd.";

    /// <summary>A field ID in the file must have the form of a custom field ID. One given by an option is looked up in Jira's field list instead.</summary>
    private static Func<string, bool, string?> FieldIdProblem(string example) =>
        (fieldId, fromOption) => fromOption || ConfigFile.IsCustomFieldId(fieldId) ? null : $" must be a custom field ID such as {example}.";

    private static void RequireAll(string path, params (string Option, bool Missing)[] options)
    {
        var missing = options.Where(option => option.Missing).Select(option => option.Option).ToList();
        if (missing.Count > 0)
        {
            var names = missing.Count == 1 ? missing[0] : $"{string.Join(", ", missing.Take(missing.Count - 1))} and {missing[^1]}";
            throw new UserFacingException($"discover needs {names}: the configuration file {path} does not give {(missing.Count == 1 ? "it" : "them")} yet.");
        }
    }

    /// <summary>The site address ending in a slash, so relative addresses keep its path.</summary>
    private static Uri WithTrailingSlash(Uri site) =>
        site.AbsolutePath.EndsWith('/') ? site : new UriBuilder(site) { Path = site.AbsolutePath + "/" }.Uri;

    /// <summary>
    /// Writes one setting and notes the change when the file held another value. The values are shown unless
    /// <paramref name="showValues"/> is false: an old site address is never printed.
    /// </summary>
    private static void Set(JsonObject section, string name, JsonNode value, string setting, List<string> changes, bool showValues = true)
    {
        if (section[name] is { } before && !JsonNode.DeepEquals(before, value))
        {
            changes.Add(showValues
                ? $"Changed {setting}: was {before.ToJsonString()}, now {value.ToJsonString()}."
                : $"Changed {setting} (the old address is not shown).");
        }

        section[name] = value;
    }

    private static string? TextOf(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
