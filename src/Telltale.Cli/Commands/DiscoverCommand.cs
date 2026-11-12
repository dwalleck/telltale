using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Telltale.Collectors.Jira;
using Telltale.Core;
using Telltale.Core.Configuration;

namespace Telltale.Cli.Commands;

/// <summary>
/// <c>discover</c>: reads a Jira site and writes the configuration file, so the owner does not look up IDs by
/// hand. The status roles it proposes are meant to be reviewed and edited, so a rerun starts from the file
/// as it is: it keeps every role and every setting it does not write itself, and says what it changed.
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

        var command = new Command("discover", "Read the Jira site and write the configuration file. A rerun keeps what the file already says.")
        {
            siteUrl, board, timeZone, backfillStart, storyPointField,
        };
        command.SetAction((parse, cancellationToken) => CliApp.GuardAsync(services, () => DiscoverAsync(
            services,
            new Given(parse.GetValue(siteUrl), parse.GetValue(board), parse.GetValue(timeZone), parse.GetValue(backfillStart), parse.GetValue(storyPointField)),
            cancellationToken)));

        return command;
    }

    /// <summary>What the command line gave. Null means "take it from the file".</summary>
    private sealed record Given(string? SiteUrl, long? Board, string? TimeZone, string? BackfillStart, string? StoryPointField);

    private static async Task DiscoverAsync(AppServices services, Given given, CancellationToken cancellationToken)
    {
        var path = services.ConfigPath;
        var previous = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;

        // Everything that can be refused is refused before the first call to Jira and before the file is touched.
        var root = previous is null ? [] : ReadExisting(previous, path);
        var jira = Section(root, "jira", path);
        var statusRoles = Section(root, "statusRoles", path);

        var siteText = given.SiteUrl ?? TextOf(jira["siteUrl"]);
        var boardId = given.Board ?? NumberOf(jira["boardId"]);
        var timeZone = given.TimeZone ?? TextOf(root["timeZone"]);
        var backfillStart = given.BackfillStart ?? TextOf(jira["backfillStart"]);
        RequireAll(path, ("--site-url", siteText is null), ("--board", boardId is null), ("--time-zone", timeZone is null), ("--backfill-start", backfillStart is null));

        var site = ReadSite(siteText!);
        if (boardId <= 0)
        {
            throw new UserFacingException("--board must be a positive number.");
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZone!, out _))
        {
            throw new UserFacingException($"--time-zone: '{timeZone}' is not a known IANA time zone ID.");
        }

        if (!DateOnly.TryParseExact(backfillStart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new UserFacingException("--backfill-start must be a date written as yyyy-MM-dd.");
        }

        var facts = await new JiraDiscovery(services.HttpHandler, site, boardId!.Value, services.Secrets).ReadAsync(cancellationToken);

        // Whatever would make the file useless stops the command here, before anything is printed or written.
        var (storyPointFieldId, storyPointSource) = ChooseStoryPointField(given.StoryPointField, TextOf(jira["storyPointFieldId"]), facts.StoryPointFields);
        if (facts.SprintFields.Count == 0)
        {
            throw new UserFacingException("Jira's field list has no Sprint field. Check that the site has Jira Software and that this account may see the field. Nothing was written.");
        }

        RequireUsableProjectKeys(facts.ProjectKeys, boardId.Value);

        var output = services.Out;
        await ListStatusesAsync(output, facts.Statuses, statusRoles);

        await output.WriteLineAsync("Resolutions (ID, name):");
        foreach (var resolution in facts.Resolutions)
        {
            await output.WriteLineAsync($"  {resolution.Id}  {resolution.Name}");
        }

        foreach (var field in facts.SprintFields)
        {
            await output.WriteLineAsync($"Sprint field: {field.Id} ({field.Name})");
        }

        await output.WriteLineAsync($"Story point field: {storyPointFieldId} ({storyPointSource})");
        await output.WriteLineAsync($"Project keys: {string.Join(", ", facts.ProjectKeys)}");

        var changes = new List<string>();
        Set(jira, "siteUrl", site.AbsoluteUri, "jira.siteUrl", changes);
        Set(jira, "boardId", boardId.Value, "jira.boardId", changes);
        Set(jira, "projectKeys", new JsonArray([.. facts.ProjectKeys.Select(key => (JsonNode)key)]), "jira.projectKeys", changes);
        Set(jira, "storyPointFieldId", storyPointFieldId, "jira.storyPointFieldId", changes);
        Set(jira, "backfillStart", backfillStart!, "jira.backfillStart", changes);
        Set(root, "timeZone", timeZone!, "timeZone", changes);

        foreach (var change in changes)
        {
            await output.WriteLineAsync(change);
        }

        var text = root.ToJsonString(Indented) + Environment.NewLine;
        if (text == previous)
        {
            // Rewriting would replace the backup of the last real change with a copy of this same file.
            await output.WriteLineAsync($"{path} already says all of this; it was not rewritten.");
            return;
        }

        if (previous is not null)
        {
            File.Copy(path, path + ".bak", overwrite: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, text, cancellationToken);
        await output.WriteLineAsync(previous is null
            ? $"Wrote {path}. Review the status roles before the first run."
            : $"Wrote {path}. The previous file is kept as {path}.bak.");
    }

    /// <summary>
    /// Lists every status with its role and gives a role to each status the file does not mention yet.
    /// A role already in the file is the owner's and stays, whatever Jira's category says.
    /// </summary>
    private static async Task ListStatusesAsync(TextWriter output, IReadOnlyList<DiscoveredStatus> statuses, JsonObject statusRoles)
    {
        await output.WriteLineAsync("Statuses (ID, name, Jira category, role):");
        foreach (var status in statuses)
        {
            var proposal = ProposeRole(status.CategoryKey);
            if (statusRoles[status.Id] is { } inFile)
            {
                var role = TextOf(inFile) ?? inFile.ToJsonString();
                var differs = proposal is not null && proposal != role ? $"; Jira's category proposes {proposal}" : "";
                await output.WriteLineAsync($"  {status.Id}  {status.Name}  {status.CategoryKey}  {role} (from the file{differs})");
            }
            else if (proposal is null)
            {
                // The file takes one of the four roles or nothing. The run stops only if an issue has been in this status.
                await output.WriteLineAsync($"  {status.Id}  {status.Name}  {status.CategoryKey}  no role (Jira's category proposes none; give it one in the file if issues use this status)");
            }
            else
            {
                statusRoles[status.Id] = proposal;
                await output.WriteLineAsync($"  {status.Id}  {status.Name}  {status.CategoryKey}  {proposal} (proposed)");
            }
        }

        var listed = statuses.Select(status => status.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (statusId, inFile) in statusRoles.Where(role => !listed.Contains(role.Key)))
        {
            await output.WriteLineAsync($"  {statusId}  (not in Jira's status list)  {(inFile is null ? "null" : TextOf(inFile) ?? inFile.ToJsonString())} (from the file)");
        }
    }

    /// <summary>
    /// The role Jira's status category suggests. Jira has no category for blocked, so a blocked status is
    /// proposed as work and the owner corrects it.
    /// </summary>
    private static string? ProposeRole(string categoryKey) => categoryKey switch
    {
        "new" => "not started",
        "indeterminate" => "work",
        "done" => "done",
        _ => null,
    };

    /// <summary>
    /// The story point field and where it came from. The owner's word wins: the option, then the file. Jira's
    /// list decides only when it names exactly one field, because a site often has two and the board uses one.
    /// </summary>
    private static (string Id, string Source) ChooseStoryPointField(string? given, string? inFile, IReadOnlyList<DiscoveredField> listed)
    {
        var names = string.Join(", ", listed.Select(field => $"{field.Id} ({field.Name})"));
        if (given is not null)
        {
            return (given, "given with --story-point-field");
        }

        if (inFile is not null)
        {
            return (inFile, listed.Count == 0 || listed.Any(field => field.Id == inFile) ? "from the file" : $"from the file; Jira lists {names}");
        }

        return listed.Count switch
        {
            1 => (listed[0].Id, listed[0].Name),
            0 => throw new UserFacingException("Jira's field list has no field this tool recognises as story points. Give the field's ID with --story-point-field. Nothing was written."),
            _ => throw new UserFacingException($"Jira lists more than one story point field: {names}. Say which one the board uses with --story-point-field. Nothing was written."),
        };
    }

    /// <summary>The run refuses a file without project keys or with a key not in Jira's form, so discover does not write one.</summary>
    private static void RequireUsableProjectKeys(IReadOnlyList<string> keys, long boardId)
    {
        if (keys.Count == 0)
        {
            throw new UserFacingException($"Jira lists no project for board {boardId.ToString(CultureInfo.InvariantCulture)}, so there is nothing to search. Check the board ID. Nothing was written.");
        }

        try
        {
            JiraProjectKey.RequireValid(keys);
        }
        catch (FormatException exception)
        {
            throw new UserFacingException($"Jira lists a project of board {boardId.ToString(CultureInfo.InvariantCulture)} whose key this tool cannot use: {exception.Message} Nothing was written.", exception);
        }
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

    private static void RequireAll(string path, params (string Option, bool Missing)[] options)
    {
        var missing = options.Where(option => option.Missing).Select(option => option.Option).ToList();
        if (missing.Count > 0)
        {
            var names = missing.Count == 1 ? missing[0] : $"{string.Join(", ", missing.Take(missing.Count - 1))} and {missing[^1]}";
            throw new UserFacingException($"discover needs {names}: the configuration file {path} does not give {(missing.Count == 1 ? "it" : "them")} yet.");
        }
    }

    /// <summary>
    /// The site address, under the rules the run applies to <c>jira.siteUrl</c>: https only, because the Jira
    /// email and API token travel with every request. It ends in a slash so relative addresses keep its path.
    /// </summary>
    private static Uri ReadSite(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var site) || site.Scheme != Uri.UriSchemeHttps)
        {
            throw new UserFacingException("--site-url must be an absolute https:// address.");
        }

        // The address is written to the file, and the file holds no secrets.
        if (site.UserInfo.Length > 0)
        {
            throw new UserFacingException("--site-url must not carry a user name or password.");
        }

        return site.AbsolutePath.EndsWith('/') ? site : new UriBuilder(site) { Path = site.AbsolutePath + "/" }.Uri;
    }

    /// <summary>Writes one setting and notes the change when the file held another value.</summary>
    private static void Set(JsonObject section, string name, JsonNode value, string setting, List<string> changes)
    {
        if (section[name] is { } before && before.ToJsonString() != value.ToJsonString())
        {
            changes.Add($"Changed {setting}: was {before.ToJsonString()}, now {value.ToJsonString()}");
        }

        section[name] = value;
    }

    private static string? TextOf(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static long? NumberOf(JsonNode? node) => node is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;
}
