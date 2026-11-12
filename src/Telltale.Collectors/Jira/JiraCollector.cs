using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Telltale.Core;
using Telltale.Core.Configuration;
using Telltale.Core.Identity;
using Telltale.Core.Intervals;
using Telltale.Core.Secrets;
using Telltale.Core.Storage;
using static Telltale.Collectors.Jira.JiraJson;

namespace Telltale.Collectors.Jira;

/// <summary>How much <see cref="JiraCollector"/> fetches.</summary>
public enum CollectionMode
{
    /// <summary>
    /// Every issue updated since the configured backfill start date, whatever the store already holds. What is
    /// fetched replaces the raw tables' rows, so nothing that has left Jira stays (owner decision D4).
    /// </summary>
    Backfill,

    /// <summary>
    /// Only issues updated since the watermark, going back two days before it. With no watermark yet
    /// (nothing was ever fetched) it fetches what a backfill would.
    /// </summary>
    Incremental,
}

/// <summary>
/// Brings the store up to date with the configured Jira board: its sprints and statuses, the issues
/// of the configured projects, and each issue's status, Sprint field and story point history from the bulk changelog endpoint
/// (ADR-0004). Only selected fields are kept (ADR-0002), and the assignee's account ID is hashed
/// before anything leaves this class (ADR-0003).
/// </summary>
public sealed class JiraCollector
{
    // The bulk changelog endpoint takes at most 1000 issues per request.
    private const int ChangelogIssuesPerRequest = 1000;

    // Page sizes asked for. Jira may return fewer on any page, so every list is read until its last page.
    // Search: the documented 5000 holds only for pages of IDs or keys alone; with named fields a real site
    // is reported to cap a page near 100 (undocumented), so more is not asked for.
    // Bulk changelog: the documented maximum.
    private const int SearchPageSize = 100;

    // The most pages read from one token-paged list: 200,000 issues at the search's page size, far more than
    // a team's board holds. Whether a real site can need more is confirmed on the work laptop (ticket 13).
    private const int MaxPagesPerList = 2000;
    private const int ChangelogPageSize = 10000;

    // The status field's ID, which is also the name old history gives it, and the Sprint field's name there.
    private const string StatusFieldId = "status";
    private const string SprintFieldName = "Sprint";

    /// <summary>The name of this collector's row in the store's watermarks.</summary>
    public const string WatermarkName = "jira";

    // Owner decision D26. Each incremental fetch starts this far before the watermark, for two reasons.
    // Jira reads a JQL date in the time zone of the account asking, which the tool does not know: the bound
    // is written in UTC, and an account can be up to 14 hours ahead of UTC or 12 behind. And Jira's search
    // is eventually consistent: an issue updated just before the newest one seen can be missing from that answer.
    private static readonly TimeSpan WatermarkOverlap = TimeSpan.FromHours(48);

    private readonly HttpMessageHandler _handler;
    private readonly JiraConfig _config;
    private readonly ISecretStore _secrets;
    private readonly TimeProvider _clock;
    private readonly TextWriter _notices;

    /// <param name="clock">Times the waits Jira asks for when it limits the rate of requests.</param>
    /// <param name="notices">Where each such wait is announced: standard error.</param>
    public JiraCollector(HttpMessageHandler handler, JiraConfig config, ISecretStore secrets, TimeProvider clock, TextWriter notices)
    {
        _clock = clock;
        _notices = notices;
        _handler = handler;
        _config = config;
        _secrets = secrets;
    }

    /// <param name="allowEmpty">
    /// Lets a backfill replace stored issues or sprints with none. Without it such a backfill is refused:
    /// Jira returning nothing is far more often a wrong setting or lost access than an empty board.
    /// </param>
    public async Task BringUpToDateAsync(ITelltaleStore store, CollectionMode mode, bool allowEmpty, CancellationToken cancellationToken = default)
    {
        // What the watermark stands for is checked before anything is asked of Jira. It is only true for the
        // site, board, projects, start date and history fields it was fetched under; continuing from it under
        // other settings would pass over every older issue the new settings bring in, without a sign.
        var scope = ScopeOf(_config);
        var watermark = await store.ReadWatermarkAsync(WatermarkName, cancellationToken);
        if (mode == CollectionMode.Incremental)
        {
            watermark?.Scope.RequireSameAs(scope);
        }

        // All three secrets are checked before the first call, so a missing salt cannot surface after a long fetch.
        var salt = _secrets.Require(SecretName.HashingSalt);

        // A sync adds to what the store holds, so it must hash with the salt the stored people were hashed with.
        // Asked here so a sync under another salt stops before it fetches; the store asks again when it
        // writes. A backfill fetches everyone again and replaces every stored row, so it may bring a new salt.
        if (mode == CollectionMode.Incremental)
        {
            IdentityHash.RequireSaltOf(await store.ReadSaltFingerprintAsync(cancellationToken), salt);
        }

        using var jira = new JiraClient(_handler, _config.SiteUrl, _secrets.Require(SecretName.JiraEmail), _secrets.Require(SecretName.JiraApiToken), _clock, _notices);

        var statuses = await FetchStatusesAsync(jira, cancellationToken);
        await RequireConfiguredFieldsAsync(jira, cancellationToken);
        var sprints = await FetchSprintsAsync(jira, cancellationToken);
        var since = mode == CollectionMode.Incremental ? watermark?.UpdatedThrough : null;
        var (issues, outOfScopeIssueIds) = await FetchIssuesAsync(jira, salt, UpdatedSince(since), cancellationToken);
        if (mode == CollectionMode.Backfill && !allowEmpty)
        {
            await RequireSomethingToReplaceWithAsync(store, issues.Count, sprints.Count, cancellationToken);
        }

        var changes = await FetchChangesAsync(jira, issues, cancellationToken);

        // The watermark is Jira's own time, the newest update fetched, and is written with the batch: if anything
        // above or the write itself fails, it stays where it was and the next sync fetches the same issues again.
        // A fetch that found nothing reaches no further than the one before it. An out-of-scope issue's update
        // does not move it: the overlap brings that issue back, and deleting it again changes nothing.
        var reached = new Watermark(WatermarkName, scope, issues.Count > 0 ? issues.Max(issue => issue.Updated) : since);
        // A backfill asked for everything, so its batch replaces what the store holds; a sync's is added to it.
        var batch = new RawBatch(new RawData(issues, changes, sprints, statuses), reached, IdentityHash.SaltFingerprint(salt))
        {
            OutOfScopeIssueIds = outOfScopeIssueIds,
            ReplacesEverything = mode == CollectionMode.Backfill,
        };
        await store.WriteRawAsync(batch, cancellationToken);
    }

    /// <summary>
    /// Stops a backfill that fetched no issue, or no sprint, while the store holds some: writing it would remove
    /// them all. Jira answers a wrong project key or board ID, or an account without access, with an empty
    /// list and no error. Fewer than before is not refused: the counts the command prints are the sign of that.
    /// </summary>
    private static async Task RequireSomethingToReplaceWithAsync(ITelltaleStore store, int issuesFetched, int sprintsFetched, CancellationToken cancellationToken)
    {
        if (issuesFetched > 0 && sprintsFetched > 0)
        {
            return;
        }

        var held = await store.CountRawAsync(cancellationToken);
        var finding = issuesFetched == 0 && held.Issues > 0 ? $"Jira returned no issues to this backfill, but the store holds {held.Issues.ToString(CultureInfo.InvariantCulture)}."
            : sprintsFetched == 0 && held.Sprints > 0 ? $"Jira returned no sprints to this backfill, but the store holds {held.Sprints.ToString(CultureInfo.InvariantCulture)}."
            : null;
        if (finding is not null)
        {
            throw new UserFacingException(
                $"{finding} A backfill replaces what the store holds, so this one would remove them all. "
                + "Check jira.projectKeys and jira.boardId in the configuration file, and that the Jira account can still see the projects and the board. "
                + "Nothing was stored. If Jira really holds none, run backfill --allow-empty.");
        }
    }

    /// <summary>
    /// What a fetch under <paramref name="config"/> asks for: the store records it with the rows it writes,
    /// and every command compares it with what the store was last filled from (<see cref="CollectionScope.RequireSameAs"/>).
    /// </summary>
    public static CollectionScope ScopeOf(JiraConfig config) => new(
        config.SiteUrl.AbsoluteUri,
        config.BoardId,
        string.Join(",", config.ProjectKeys.Order(StringComparer.Ordinal)),
        config.BackfillStart,
        $"{StatusFieldId},{config.SprintFieldId},{config.StoryPointFieldId}");

    /// <summary>
    /// The earliest moment from which a store filled under <paramref name="scope"/> holds every issue update:
    /// the start of its start date. Jira reads that date in the time zone of the account asking, which the
    /// tool does not know, so this is where the date begins last, twelve hours behind UTC. Null when the scope
    /// names no start date. A sprint that started earlier can lack issues delivered in it.
    /// </summary>
    public static DateTimeOffset? CompleteFrom(CollectionScope scope) =>
        scope.StartDate is { } start ? new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-12)) : null;

    /// <summary>
    /// The JQL value for the lower bound on <c>updated</c>, a quoted JQL date. From a watermark it is the
    /// watermark less the overlap, in UTC, to the minute (JQL has no finer form). Without one it is the
    /// configured start date. Jira reads either in the calling account's time zone, so neither is an exact
    /// instant: the overlap absorbs that for the first, and the second is a whole day.
    /// </summary>
    private string UpdatedSince(DateTimeOffset? watermark) => watermark is { } reached
        ? $"\"{(reached - WatermarkOverlap).UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}\""
        : $"\"{_config.BackfillStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}\"";

    private static async Task<List<RawStatus>> FetchStatusesAsync(JiraClient jira, CancellationToken cancellationToken)
    {
        using var document = await jira.GetAsync("rest/api/3/status", cancellationToken);
        return Read<List<RawStatus>>("status list", () => [.. document.RootElement.EnumerateArray().Select(status => new RawStatus(
            Text(status, "id"),
            Text(status, "name"),
            Text(status.GetProperty("statusCategory"), "key")))]);
    }

    /// <summary>
    /// Stops when Jira's field list does not hold the Sprint field or the story point field the configuration
    /// names. Jira leaves a field it does not know out of every answer, and an absent field reads the same as
    /// an empty one: with a wrong ID every issue would be "in no sprint" or "unestimated", without a sign.
    /// Asked once per collection, before anything is fetched with the IDs. The field list's shape is the one
    /// <see cref="JiraDiscovery"/> reads, from Atlassian's documentation; a real site confirms it (ticket 13).
    /// </summary>
    private async Task RequireConfiguredFieldsAsync(JiraClient jira, CancellationToken cancellationToken)
    {
        using var document = await jira.GetAsync("rest/api/3/field", cancellationToken);
        var listed = Read("field list", () => document.RootElement.EnumerateArray().Select(field => Text(field, "id")).ToHashSet(StringComparer.Ordinal));
        var finding = !listed.Contains(_config.SprintFieldId) ? "jira.sprintFieldId in the configuration file names a field Jira does not list, so every issue would read as in no sprint."
            : !listed.Contains(_config.StoryPointFieldId) ? "jira.storyPointFieldId in the configuration file names a field Jira does not list, so every issue would read as unestimated."
            : null;
        if (finding is not null)
        {
            throw new UserFacingException($"{finding} Correct the setting (discover finds the field) and run again. Nothing was stored.");
        }
    }

    /// <summary>
    /// The board's sprints, from every page of the list (owner decision D11). A list that cannot be followed
    /// to its end for certain stops the run (<see cref="OffsetPaging.StopsWhenInDoubt"/>).
    /// </summary>
    private async Task<List<RawSprint>> FetchSprintsAsync(JiraClient jira, CancellationToken cancellationToken)
    {
        var listed = await OffsetPages.ReadAllAsync(
            jira,
            $"rest/agile/1.0/board/{_config.BoardId.ToString(CultureInfo.InvariantCulture)}/sprint",
            pageSize: null,
            OffsetPaging.StopsWhenInDoubt,
            "sprint list",
            "Nothing was stored.",
            page => Read("sprint list", page),
            ParseSprint,
            sprint => sprint.SprintId,
            cancellationToken);

        // The board also lists sprints created on other boards when one of their issues matches its filter.
        // Those are another team's sprints; counting deliveries in them would take them from this team's.
        return [.. listed.Where(sprint => sprint.OriginBoardId is not { } origin || origin == _config.BoardId).Select(sprint => sprint.Sprint)];
    }

    private (string SprintId, long? OriginBoardId, RawSprint Sprint) ParseSprint(JsonElement sprint)
    {
        var sprintId = sprint.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture);
        return (sprintId, Optional(sprint, "originBoardId")?.GetInt64(), new RawSprint(
            sprintId,
            _config.BoardId,
            Text(sprint, "name"),
            Text(sprint, "state"),
            OptionalTime(sprint, "startDate"),
            OptionalTime(sprint, "endDate"),
            OptionalTime(sprint, "completeDate")));
    }

    private async Task<(List<RawIssue> InScope, List<string> OutOfScopeIssueIds)> FetchIssuesAsync(JiraClient jira, string salt, string updatedSince, CancellationToken cancellationToken)
    {
        // The project keys come from the configuration, not from Jira (owner decision D2). They are checked
        // again here, where they enter the JQL, so no caller can pass text that is more than a key.
        JiraProjectKey.RequireValid(_config.ProjectKeys);
        var projects = string.Join(", ", _config.ProjectKeys.Select(key => $"\"{key}\""));
        var body = new JsonObject
        {
            ["jql"] = $"project in ({projects}) AND updated >= {updatedSince} ORDER BY updated ASC, id ASC",

            // A request, not a promise: Jira may return fewer than asked for, and the rest comes on further pages.
            ["maxResults"] = SearchPageSize,

            // Named fields only: the default is the ID alone, and free-text fields are never asked for.
            ["fields"] = new JsonArray("issuetype", "status", "resolution", "assignee", "created", "updated", _config.SprintFieldId, _config.StoryPointFieldId),
        };

        // By issue ID, the issue as it was last seen, or null when it was out of scope then. The search is
        // ordered by update time and then by ID, so issues updated in the same instant keep their places
        // between pages. An issue updated while the pages are read moves to the end and is met again on a
        // later page; the later sighting is the newer one.
        var seen = new Dictionary<string, RawIssue?>(StringComparer.Ordinal);
        await ForEachPageAsync(jira, "rest/api/3/search/jql", body, "issue search", page => ParseIssues(page, salt, seen), cancellationToken);
        return ([.. seen.Values.OfType<RawIssue>()], [.. seen.Where(issue => issue.Value is null).Select(issue => issue.Key)]);
    }

    /// <summary>
    /// The page's issues that are in scope: every issue type except epics and sub-tasks (system design, "Scope").
    /// The others are dropped here, so nothing of them is stored, no history is fetched for them, they can
    /// appear in no figure, and a field of theirs in an unknown shape cannot stop the run. Only their ID is
    /// read, so the store can delete an issue it holds from the time before it was converted (owner decision D13).
    /// </summary>
    private int ParseIssues(JsonElement page, string salt, Dictionary<string, RawIssue?> seen)
    {
        var issues = page.GetProperty("issues");
        foreach (var issue in issues.EnumerateArray())
        {
            var fields = issue.GetProperty("fields");
            var issueType = fields.GetProperty("issuetype");
            var issueId = IssueId(issue);
            if (!IsInScope(issueType))
            {
                seen[issueId] = null;
                continue;
            }

            seen[issueId] = new RawIssue
            {
                IssueId = issueId,
                IssueTypeId = Text(issueType, "id"),
                Created = JiraTime.Parse(fields.GetProperty("created")),
                StatusId = Text(fields.GetProperty("status"), "id"),
                ResolutionId = Optional(fields, "resolution") is { } resolution ? Text(resolution, "id") : null,
                StoryPoints = Optional(fields, _config.StoryPointFieldId) is { } points ? WithoutSign(points.GetDouble()) : null,
                AssigneeHash = Optional(fields, "assignee") is { } assignee ? IdentityHash.Of(Text(assignee, "accountId"), salt) : null,
                Updated = JiraTime.Parse(fields.GetProperty("updated")),
                SprintIds = Optional(fields, _config.SprintFieldId) is { } sprints ? SprintIds(sprints) : "",
            };
        }

        return issues.GetArrayLength();
    }

    /// <summary>
    /// True for a standard issue type. Jira gives each type a hierarchy level: negative for sub-tasks, 0 for
    /// standard types, 1 for epics and more for what sits above epics; those are containers too. A sub-task
    /// also carries <c>subtask: true</c>, which decides on its own, with or without a level. A type that is
    /// not flagged as a sub-task and has no level is an answer in an unknown shape: guessing would let an epic
    /// into the figures unnoticed.
    /// </summary>
    private static bool IsInScope(JsonElement issueType) =>
        Optional(issueType, "subtask")?.GetBoolean() != true
        && issueType.GetProperty("hierarchyLevel").GetInt32() == 0;

    /// <summary>
    /// The Sprint field's value in the stored form of sprint IDs (<see cref="SprintMembership.StoredSprintIds"/>),
    /// which a stored Sprint change has too. The field is an array with
    /// one object per sprint; only the IDs are kept. An issue in no sprint (no value, or an empty array) gives
    /// an empty text, never null: null is kept for issues stored before the field was fetched.
    /// </summary>
    private static string SprintIds(JsonElement sprints) =>
        SprintMembership.StoredSprintIds(sprints.EnumerateArray().Select(sprint => sprint.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture)));

    /// <summary>True when a status change's value is what the status field's history holds: nothing, or a status ID (digits).</summary>
    private static bool IsStatusId(string? value) => string.IsNullOrEmpty(value) || value.All(char.IsAsciiDigit);

    /// <summary>
    /// One side of a story point change as the store keeps it: the number in round-trip form, or null for no
    /// estimate. <paramref name="id"/> is the item's <c>from</c> or <c>to</c> and <paramref name="text"/> its
    /// <c>fromString</c> or <c>toString</c>. The estimate may be in either or in both; in both, they must be
    /// the same number, because a select list also has a number (its option ID) beside a name. Anything that is
    /// not an estimate stops the run before anything is kept, and the message quotes no value.
    /// </summary>
    private static string? PointValue(string? id, string? text)
    {
        var fromId = Estimate(id);
        var fromText = Estimate(text);
        if (fromId is not null && fromText is not null && fromId != fromText)
        {
            throw NotAnEstimate();
        }

        return (fromId ?? fromText)?.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>The estimate a text holds: null for an empty one, a number that is finite and not negative otherwise.</summary>
    private static double? Estimate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var points) || !double.IsFinite(points) || points < 0)
        {
            throw NotAnEstimate();
        }

        return WithoutSign(points);
    }

    /// <summary>Zero for negative zero, which Jira can send and which would be stored and labelled as "-0"; any other number as it is.</summary>
    private static double WithoutSign(double points) => points == 0 ? 0 : points;

    // Two causes look the same from here: the setting names a field that is not the story point field (its
    // history may hold names or free text), or Jira writes the number in a form not read here, such as a
    // decimal comma. The real form is confirmed on the work laptop (ticket 13).
    private static UserFacingException NotAnEstimate() => new(
        "The history of the field named by jira.storyPointFieldId in the configuration file holds a value that is not a story point number this tool reads. "
        + "Either the setting names another field, or Jira writes the number in another form. Nothing was stored.");

    /// <summary>One of the four values of a story point changelog item: text, as the schema says, or a JSON number.</summary>
    private static string? PointText(JsonElement item, string name) => Optional(item, name) switch
    {
        { ValueKind: JsonValueKind.Number } number => number.GetRawText(),
        var value => value?.GetString(),
    };

    private async Task<List<RawChange>> FetchChangesAsync(JiraClient jira, List<RawIssue> issues, CancellationToken cancellationToken)
    {
        var changes = new List<RawChange>();

        foreach (var chunk in issues.Chunk(ChangelogIssuesPerRequest))
        {
            var body = new JsonObject
            {
                ["issueIdsOrKeys"] = new JsonArray([.. chunk.Select(issue => (JsonNode)issue.IssueId)]),
                ["fieldIds"] = new JsonArray(StatusFieldId, _config.SprintFieldId, _config.StoryPointFieldId),
                ["maxResults"] = ChangelogPageSize,
            };

            // One issue's history can be spread over several pages: entries come oldest first, not issue by issue.
            await ForEachPageAsync(jira, "rest/api/3/changelog/bulkfetch", body, "bulk changelog", page => ParseChanges(page, changes), cancellationToken);
        }

        return changes;
    }

    private int ParseChanges(JsonElement page, List<RawChange> changes)
    {
        var logs = page.GetProperty("issueChangeLogs");
        foreach (var log in logs.EnumerateArray())
        {
            var issueId = Text(log, "issueId");
            foreach (var history in log.GetProperty("changeHistories").EnumerateArray())
            {
                var historyId = Text(history, "id");
                var at = JiraTime.Parse(history.GetProperty("created"));

                foreach (var item in history.GetProperty("items").EnumerateArray())
                {
                    var field = FieldOf(item);

                    if (field is null)
                    {
                        continue;
                    }

                    if (field == ChangeFields.Points)
                    {
                        // A number field's history is expected to carry its numbers in fromString and toString and
                        // leave from and to empty; the documentation does not settle it, so both places are read.
                        // Only a number is ever kept from them.
                        changes.Add(new RawChange(
                            historyId,
                            issueId,
                            at,
                            field,
                            PointValue(PointText(item, "from"), PointText(item, "fromString")),
                            PointValue(PointText(item, "to"), PointText(item, "toString"))));
                        continue;
                    }

                    var from = Optional(item, "from")?.GetString();
                    var to = Optional(item, "to")?.GetString();

                    // Checked before anything is kept, and no message quotes a value. A status change holds
                    // status IDs, which are digits: anything else is not the status field's history.
                    if (field == ChangeFields.Status && !(IsStatusId(from) && IsStatusId(to)))
                    {
                        throw new FormatException("A status change holds a value that is not a status ID.");
                    }

                    // A Sprint change's value is what the Sprint field's history holds: nothing, or sprint IDs
                    // (digits) separated by commas, which Jira writes as "41, 42" and the store keeps as "41,42".
                    // Anything else means the configured field is another field, whose history may hold account
                    // IDs or free text.
                    if (field == ChangeFields.Sprint && !(SprintMembership.TryNormalizeSprintIds(from, out from) && SprintMembership.TryNormalizeSprintIds(to, out to)))
                    {
                        throw new UserFacingException(
                            "The history of the field named by jira.sprintFieldId in the configuration file holds values that are not sprint IDs, so it is not the Sprint field. Nothing was stored.");
                    }

                    changes.Add(new RawChange(historyId, issueId, at, field, from, to));
                }
            }
        }

        return logs.GetArrayLength();
    }

    /// <summary>
    /// Which kept field a changelog item is about, or null for any other field. The item's <c>fieldId</c>
    /// decides. Old history carries none, only the field's name and whether it is a system field
    /// (<c>fieldtype</c> "jira") or a custom one ("custom"): there the status field is the system field named
    /// "status", and the Sprint field is the custom field named "Sprint", Jira Software's own name for it. An
    /// item that does not say which kind it is, is neither. A site that renamed its Sprint field loses the
    /// sprint changes of that old history, and one with a second custom field named "Sprint" stops on that
    /// field's values: the name is all such an item has. The story point field has no name to go by (sites
    /// name it differently), so an old item of it is not read.
    /// </summary>
    private string? FieldOf(JsonElement item)
    {
        if (Optional(item, "fieldId")?.GetString() is { } fieldId)
        {
            return fieldId == StatusFieldId ? ChangeFields.Status
                : fieldId == _config.SprintFieldId ? ChangeFields.Sprint
                : fieldId == _config.StoryPointFieldId ? ChangeFields.Points
                : null;
        }

        return (Optional(item, "field")?.GetString(), Optional(item, "fieldtype")?.GetString()) switch
        {
            (StatusFieldId, "jira") => ChangeFields.Status,
            (SprintFieldName, "custom") => ChangeFields.Sprint,
            _ => null,
        };
    }

    /// <summary>
    /// Reads Jira's answer. An answer in a shape the tool does not know (a missing field, a value of
    /// another type, a timestamp in an unknown form) stops the run with a plain message. The message
    /// names only the kind of failure, never a value from the answer.
    /// </summary>
    private static T Read<T>(string what, Func<T> parse)
    {
        try
        {
            return parse();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new UserFacingException($"Jira's {what} answered in a shape this tool does not understand ({exception.GetType().Name}). Nothing was stored.", exception);
        }
    }

    /// <summary>
    /// An issue's ID. Jira's issue IDs are digits; anything else is an answer in an unknown shape. The ID is
    /// stored and written to the anomaly log as it is, so no other text may travel in it.
    /// </summary>
    private static string IssueId(JsonElement issue)
    {
        var id = Text(issue, "id");
        return id.Length > 0 && id.All(char.IsAsciiDigit) ? id : throw new FormatException("The issue ID is not digits.");
    }

    private static DateTimeOffset? OptionalTime(JsonElement parent, string name) =>
        Optional(parent, name) is { } value ? JiraTime.Parse(value) : null;

    /// <summary>
    /// Asks a token-paged endpoint for every page: the same request again, each time with the token the page
    /// before gave, until a page gives none. <paramref name="read"/> takes what it keeps from each page and
    /// returns how many entries the page held; the page itself is released before the next is asked for.
    /// </summary>
    private static async Task ForEachPageAsync(JiraClient jira, string relativeUrl, JsonObject body, string what, Func<JsonElement, int> read, CancellationToken cancellationToken)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 1; ; page++)
        {
            using var document = await jira.PostAsync(relativeUrl, body, cancellationToken);
            var (entries, next) = Read(what, () => (read(document.RootElement), NextPageToken(document.RootElement, what)));
            if (next is null)
            {
                return;
            }

            // A site that gives a new token and a page that is not empty every time passes the two checks
            // below for ever.
            if (page == MaxPagesPerList)
            {
                throw new UserFacingException(
                    $"Jira's {what} gave {MaxPagesPerList.ToString(CultureInfo.InvariantCulture)} pages and says more follow. "
                    + "This tool reads no more than that from one list, so that a list without an end cannot keep it running. Nothing was stored.");
            }

            // A site that keeps giving new tokens and nothing else would otherwise be asked forever.
            if (entries == 0)
            {
                throw new UserFacingException($"Jira's {what} gave an empty page and says more follow, so entries could be missing. Nothing was stored.");
            }

            // Jira would have to repeat itself for this, but then the run would never end.
            if (!tokens.Add(next))
            {
                throw new UserFacingException($"Jira's {what} gave the same page token twice, so its pages would never end. Nothing was stored.");
            }

            body["nextPageToken"] = next;
        }
    }

    /// <summary>
    /// The token for the page after this one, or null on the last page. A last page has no
    /// <c>nextPageToken</c>, or a null one; both are documented. The search also says <c>isLast</c>; when it
    /// does, it must agree with the token, or nothing says for certain whether issues are still to come.
    /// </summary>
    private static string? NextPageToken(JsonElement page, string what)
    {
        var token = Optional(page, "nextPageToken")?.GetString();
        var isLast = Optional(page, "isLast")?.GetBoolean();
        if (isLast is { } last && last != (token is null))
        {
            throw new UserFacingException(last
                ? $"Jira's {what} says a page is the last one and also gives a token for a further page. Nothing was stored."
                : $"Jira's {what} says a further page follows but gives no token for it. Nothing was stored.");
        }

        return token;
    }
}
