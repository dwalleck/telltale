using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Telltale.Core;
using Telltale.Core.Configuration;
using Telltale.Core.Identity;
using Telltale.Core.Secrets;
using Telltale.Core.Storage;

namespace Telltale.Collectors.Jira;

/// <summary>How much <see cref="JiraCollector"/> fetches.</summary>
public enum CollectionMode
{
    /// <summary>Every issue updated since the configured backfill start date, whatever the store already holds.</summary>
    Backfill,

    /// <summary>
    /// Only issues updated since the watermark, going back two days before it. With no watermark yet
    /// (nothing was ever fetched) it fetches what a backfill would.
    /// </summary>
    Incremental,
}

/// <summary>
/// Brings the store up to date with the configured Jira board: its sprints and statuses, the issues
/// of the configured projects, and each issue's status and Sprint field history from the bulk changelog endpoint
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
    private const int ChangelogPageSize = 10000;

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

    public JiraCollector(HttpMessageHandler handler, JiraConfig config, ISecretStore secrets)
    {
        _handler = handler;
        _config = config;
        _secrets = secrets;
    }

    public async Task BringUpToDateAsync(ITelltaleStore store, CollectionMode mode, CancellationToken cancellationToken = default)
    {
        // What the watermark stands for is checked before anything is asked of Jira. It is only true for the
        // board, projects and start date it was fetched under; continuing from it under other settings would
        // pass over every older issue the new settings bring in, without a sign.
        var covers = Covers();
        var watermark = await store.ReadWatermarkAsync(WatermarkName, cancellationToken);
        if (mode == CollectionMode.Incremental && watermark is not null && watermark.Covers != covers)
        {
            throw new UserFacingException("boardId, projectKeys or backfillStart in the configuration file changed since the store was last filled, so a sync could miss issues. Run backfill.");
        }

        // All three secrets are checked before the first call, so a missing salt cannot surface after a long fetch.
        var salt = _secrets.Require(SecretName.HashingSalt);
        using var jira = new JiraClient(_handler, _config.SiteUrl, _secrets.Require(SecretName.JiraEmail), _secrets.Require(SecretName.JiraApiToken));

        var statuses = await FetchStatusesAsync(jira, cancellationToken);
        var sprints = await FetchSprintsAsync(jira, cancellationToken);
        var since = mode == CollectionMode.Incremental ? watermark?.UpdatedThrough : null;
        var (issues, outOfScopeIssueIds) = await FetchIssuesAsync(jira, salt, UpdatedSince(since), cancellationToken);
        var changes = await FetchChangesAsync(jira, issues, cancellationToken);

        // The watermark is Jira's own time, the newest update fetched, and is written with the batch: if anything
        // above or the write itself fails, it stays where it was and the next sync fetches the same issues again.
        // A fetch that found nothing reaches no further than the one before it. An out-of-scope issue's update
        // does not move it: the overlap brings that issue back, and deleting it again changes nothing.
        var reached = new Watermark(WatermarkName, covers, issues.Count > 0 ? issues.Max(issue => issue.Updated) : since);
        await store.WriteRawAsync(new RawData(issues, changes, sprints, statuses) { OutOfScopeIssueIds = outOfScopeIssueIds }, reached, cancellationToken);
    }

    /// <summary>What a fetch under the current configuration covers: the settings that decide which issues and sprints are asked for.</summary>
    private string Covers() =>
        $"board {_config.BoardId.ToString(CultureInfo.InvariantCulture)}; projects {string.Join(",", _config.ProjectKeys.Order(StringComparer.Ordinal))}; from {_config.BackfillStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

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
    /// The board's sprints, from every page of the list (owner decision D11). The Agile API pages by offset:
    /// each request names the position to start at, which is the number of sprints read so far.
    /// </summary>
    private async Task<List<RawSprint>> FetchSprintsAsync(JiraClient jira, CancellationToken cancellationToken)
    {
        var sprints = new List<RawSprint>();
        var seen = new HashSet<long>();
        long startAt = 0;
        while (true)
        {
            using var document = await jira.GetAsync(
                $"rest/agile/1.0/board/{_config.BoardId.ToString(CultureInfo.InvariantCulture)}/sprint?startAt={startAt.ToString(CultureInfo.InvariantCulture)}", cancellationToken);
            var (read, isComplete) = Read("sprint list", () =>
            {
                var page = document.RootElement;
                var count = page.GetProperty("values").GetArrayLength();
                var complete = IsLastSprintPage(page, startAt, count);

                // A site that answers every position with the same page would otherwise be asked forever.
                if (count > 0 && page.GetProperty("values").EnumerateArray().Count(sprint => seen.Add(sprint.GetProperty("id").GetInt64())) == 0)
                {
                    throw new UserFacingException("Jira's sprint list gave a page holding only sprints it had already given, so its pages would never end. Nothing was stored.");
                }

                ParseSprints(page, sprints);
                return (count, complete);
            });

            if (isComplete)
            {
                return sprints;
            }

            startAt += read;
        }
    }

    /// <summary>
    /// Whether a page of the sprint list is the last one. Jira says so with <c>isLast</c>; when it leaves that
    /// out, with <c>total</c>, which it may leave out too; and with neither, the list ends at the first empty
    /// page. A page that says more follow and holds nothing, or that starts somewhere other than where it was
    /// asked to, cannot be followed: the run stops instead of dropping sprints or asking forever.
    /// </summary>
    private static bool IsLastSprintPage(JsonElement page, long startAt, int count)
    {
        if (Optional(page, "startAt") is { } startsAt && startsAt.GetInt64() != startAt)
        {
            throw new UserFacingException("Jira's sprint list gave a page that does not start where it was asked to, so sprints could be missing. Nothing was stored.");
        }

        var isLast = Optional(page, "isLast")?.GetBoolean();
        var total = Optional(page, "total")?.GetInt64();
        var complete = isLast ?? (total is { } all ? startAt + count >= all : count == 0);
        if (!complete && count == 0)
        {
            throw new UserFacingException("Jira's sprint list gave an empty page and says more sprints follow, so sprints could be missing. Nothing was stored.");
        }

        return complete;
    }

    private void ParseSprints(JsonElement page, List<RawSprint> sprints)
    {
        // The board also lists sprints created on other boards when one of their issues matches its filter.
        // Those are another team's sprints; counting deliveries in them would take them from this team's.
        sprints.AddRange(page.GetProperty("values").EnumerateArray()
            .Where(sprint => Optional(sprint, "originBoardId") is not { } origin || origin.GetInt64() == _config.BoardId)
            .Select(sprint => new RawSprint(
            sprint.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture),
            _config.BoardId,
            Text(sprint, "name"),
            Text(sprint, "state"),
            OptionalTime(sprint, "startDate"),
            OptionalTime(sprint, "endDate"),
            OptionalTime(sprint, "completeDate"))));
    }

    private async Task<(List<RawIssue> InScope, List<string> OutOfScopeIssueIds)> FetchIssuesAsync(JiraClient jira, string salt, string updatedSince, CancellationToken cancellationToken)
    {
        // The project keys come from the configuration, not from Jira (owner decision D2). They are checked
        // again here, where they enter the JQL, so no caller can pass text that is more than a key.
        JiraProjectKey.RequireValid(_config.ProjectKeys);
        var projects = string.Join(", ", _config.ProjectKeys.Select(key => $"\"{key}\""));
        var body = new JsonObject
        {
            ["jql"] = $"project in ({projects}) AND updated >= {updatedSince} ORDER BY updated ASC",

            // A request, not a promise: Jira may return fewer than asked for, and the rest comes on further pages.
            ["maxResults"] = SearchPageSize,

            // Named fields only: the default is the ID alone, and free-text fields are never asked for.
            ["fields"] = new JsonArray("issuetype", "status", "resolution", "assignee", "created", "updated", _config.SprintFieldId, _config.StoryPointFieldId),
        };

        var issues = new List<RawIssue>();
        var outOfScopeIssueIds = new List<string>();
        await ForEachPageAsync(jira, "rest/api/3/search/jql", body, "issue search", page => ParseIssues(page, salt, issues, outOfScopeIssueIds), cancellationToken);
        return (issues, outOfScopeIssueIds);
    }

    /// <summary>
    /// The page's issues that are in scope: every issue type except epics and sub-tasks (system design, "Scope").
    /// The others are dropped here, so nothing of them is stored, no history is fetched for them, they can
    /// appear in no figure, and a field of theirs in an unknown shape cannot stop the run. Only their ID is
    /// read, so the store can delete an issue it holds from the time before it was converted (owner decision D13).
    /// </summary>
    private void ParseIssues(JsonElement page, string salt, List<RawIssue> issues, List<string> outOfScopeIssueIds)
    {
        foreach (var issue in page.GetProperty("issues").EnumerateArray())
        {
            var fields = issue.GetProperty("fields");
            var issueType = fields.GetProperty("issuetype");
            if (!IsInScope(issueType))
            {
                outOfScopeIssueIds.Add(Text(issue, "id"));
                continue;
            }

            issues.Add(new RawIssue(
                Text(issue, "id"),
                Text(issue, "key"),
                Text(issueType, "id"),
                JiraTime.Parse(fields.GetProperty("created")),
                Text(fields.GetProperty("status"), "id"),
                Optional(fields, "resolution") is { } resolution ? Text(resolution, "id") : null,
                Optional(fields, _config.StoryPointFieldId)?.GetDouble(),
                Optional(fields, "assignee") is { } assignee ? IdentityHash.Of(Text(assignee, "accountId"), salt) : null,
                JiraTime.Parse(fields.GetProperty("updated")),
                Optional(fields, _config.SprintFieldId) is { } sprints ? SprintIds(sprints) : ""));
        }
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
    /// The Sprint field's value as the comma-separated IDs a Sprint change carries. The field is an array with
    /// one object per sprint; only the IDs are kept. An issue in no sprint (no value, or an empty array) gives
    /// an empty text, never null: null is kept for issues stored before the field was fetched.
    /// </summary>
    private static string SprintIds(JsonElement sprints) =>
        string.Join(",", sprints.EnumerateArray().Select(sprint => sprint.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// True when a Sprint change's value is what the Sprint field's history holds: nothing, or sprint IDs
    /// (digits) separated by commas. Anything else means the configured field is another field, whose
    /// history may hold account IDs or free text.
    /// </summary>
    private static bool IsSprintIdList(string? value) =>
        value is null || value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).All(id => id.All(char.IsAsciiDigit));

    private async Task<List<RawChange>> FetchChangesAsync(JiraClient jira, List<RawIssue> issues, CancellationToken cancellationToken)
    {
        var changes = new List<RawChange>();

        foreach (var chunk in issues.Chunk(ChangelogIssuesPerRequest))
        {
            var body = new JsonObject
            {
                ["issueIdsOrKeys"] = new JsonArray([.. chunk.Select(issue => (JsonNode)issue.IssueId)]),
                ["fieldIds"] = new JsonArray("status", _config.SprintFieldId),
                ["maxResults"] = ChangelogPageSize,
            };

            // One issue's history can be spread over several pages: entries come oldest first, not issue by issue.
            await ForEachPageAsync(jira, "rest/api/3/changelog/bulkfetch", body, "bulk changelog", page => ParseChanges(page, changes), cancellationToken);
        }

        return changes;
    }

    private void ParseChanges(JsonElement page, List<RawChange> changes)
    {
        foreach (var log in page.GetProperty("issueChangeLogs").EnumerateArray())
        {
            var issueId = Text(log, "issueId");
            foreach (var history in log.GetProperty("changeHistories").EnumerateArray())
            {
                var historyId = Text(history, "id");
                var at = JiraTime.Parse(history.GetProperty("created"));

                foreach (var item in history.GetProperty("items").EnumerateArray())
                {
                    var field = Optional(item, "fieldId")?.GetString() switch
                    {
                        "status" => ChangeFields.Status,
                        { } fieldId when fieldId == _config.SprintFieldId => ChangeFields.Sprint,
                        _ => null,
                    };

                    if (field is null)
                    {
                        continue;
                    }

                    var from = Optional(item, "from")?.GetString();
                    var to = Optional(item, "to")?.GetString();

                    // Checked before anything is kept, and the message quotes no value.
                    if (field == ChangeFields.Sprint && !(IsSprintIdList(from) && IsSprintIdList(to)))
                    {
                        throw new UserFacingException(
                            "The history of the field named by jira.sprintFieldId in the configuration file holds values that are not sprint IDs, so it is not the Sprint field. Nothing was stored.");
                    }

                    changes.Add(new RawChange(historyId, issueId, at, field, from, to));
                }
            }
        }
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

    /// <summary>A text value Jira must give. Absent, null or not text is an answer in an unknown shape.</summary>
    private static string Text(JsonElement parent, string name) =>
        parent.GetProperty(name).GetString() ?? throw new FormatException($"{name} is null.");

    /// <summary>A property's value, or null when it is absent or JSON null. Jira uses both for "not set".</summary>
    private static JsonElement? Optional(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

    private static DateTimeOffset? OptionalTime(JsonElement parent, string name) =>
        Optional(parent, name) is { } value ? JiraTime.Parse(value) : null;

    /// <summary>
    /// Asks a token-paged endpoint for every page: the same request again, each time with the token the page
    /// before gave, until a page gives none. <paramref name="read"/> takes what it keeps from each page; the
    /// page itself is released before the next is asked for.
    /// </summary>
    private static async Task ForEachPageAsync(JiraClient jira, string relativeUrl, JsonObject body, string what, Action<JsonElement> read, CancellationToken cancellationToken)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            using var document = await jira.PostAsync(relativeUrl, body, cancellationToken);
            var next = Read(what, () =>
            {
                read(document.RootElement);
                return NextPageToken(document.RootElement, what);
            });

            if (next is null)
            {
                return;
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
