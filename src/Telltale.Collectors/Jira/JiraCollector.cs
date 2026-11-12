using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Telltale.Core;
using Telltale.Core.Configuration;
using Telltale.Core.Identity;
using Telltale.Core.Intervals;
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

    // The largest page sizes Jira documents for the search and bulk changelog endpoints. Jira may return fewer.
    private const int SearchPageSize = 5000;
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

    private async Task<List<RawSprint>> FetchSprintsAsync(JiraClient jira, CancellationToken cancellationToken)
    {
        using var document = await jira.GetAsync($"rest/agile/1.0/board/{_config.BoardId}/sprint", cancellationToken);
        return Read("sprint list", () =>
        {
            RequireCompleteList(document.RootElement, "sprint list");
            return ParseSprints(document.RootElement);
        });
    }

    private List<RawSprint> ParseSprints(JsonElement page)
    {
        // The board also lists sprints created on other boards when one of their issues matches its filter.
        // Those are another team's sprints; counting deliveries in them would take them from this team's.
        return [.. page.GetProperty("values").EnumerateArray()
            .Where(sprint => Optional(sprint, "originBoardId") is not { } origin || origin.GetInt64() == _config.BoardId)
            .Select(sprint => new RawSprint(
            sprint.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture),
            _config.BoardId,
            Text(sprint, "name"),
            Text(sprint, "state"),
            OptionalTime(sprint, "startDate"),
            OptionalTime(sprint, "endDate"),
            OptionalTime(sprint, "completeDate")))];
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

            // A request, not a promise: Jira's default is 50, and it may return fewer than asked for. The docs
            // say the largest pages come with the ID or key alone; with named fields a real site is expected to
            // cap a page near 100. So a board of ordinary size still has a further page, which stops the run
            // until paging is built (ticket 10).
            ["maxResults"] = SearchPageSize,

            // Named fields only: the default is the ID alone, and free-text fields are never asked for.
            ["fields"] = new JsonArray("issuetype", "status", "resolution", "assignee", "created", "updated", _config.SprintFieldId, _config.StoryPointFieldId),
        };

        using var document = await jira.PostAsync("rest/api/3/search/jql", body, cancellationToken);
        return Read("issue search", () =>
        {
            RequireLastPage(document.RootElement, "issue search");
            return ParseIssues(document.RootElement, salt);
        });
    }

    /// <summary>
    /// The page's issues that are in scope: every issue type except epics and sub-tasks (system design, "Scope").
    /// The others are dropped here, so nothing of them is stored, no history is fetched for them, they can
    /// appear in no figure, and a field of theirs in an unknown shape cannot stop the run. Only their ID is
    /// read, so the store can delete an issue it holds from the time before it was converted (owner decision D13).
    /// </summary>
    private (List<RawIssue> InScope, List<string> OutOfScopeIssueIds) ParseIssues(JsonElement page, string salt)
    {
        var issues = new List<RawIssue>();
        var outOfScopeIssueIds = new List<string>();
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

        return (issues, outOfScopeIssueIds);
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

            using var document = await jira.PostAsync("rest/api/3/changelog/bulkfetch", body, cancellationToken);
            changes.AddRange(Read("bulk changelog", () =>
            {
                RequireLastPage(document.RootElement, "bulk changelog");
                return ParseChanges(document.RootElement);
            }));
        }

        return changes;
    }

    private List<RawChange> ParseChanges(JsonElement page)
    {
        var changes = new List<RawChange>();
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

                    // A Sprint change's value is what the Sprint field's history holds: nothing, or sprint IDs
                    // (digits) separated by commas, which Jira writes as "41, 42" and the store keeps as "41,42".
                    // Anything else means the configured field is another field, whose history may hold account
                    // IDs or free text. Checked before anything is kept, and the message quotes no value.
                    if (field == ChangeFields.Sprint && !(SprintMembership.TryNormalizeSprintIds(from, out from) && SprintMembership.TryNormalizeSprintIds(to, out to)))
                    {
                        throw new UserFacingException(
                            "The history of the field named by jira.sprintFieldId in the configuration file holds values that are not sprint IDs, so it is not the Sprint field. Nothing was stored.");
                    }

                    changes.Add(new RawChange(historyId, issueId, at, field, from, to));
                }
            }
        }

        return changes;
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

    // Paging is not built yet. Until it is, a further page stops the run instead of being dropped silently.
    // Both checks run inside Read, so an answer that is not a JSON object is reported as an unknown shape.

    /// <summary>
    /// For the Agile API's offset paging. The list is complete when <c>isLast</c> is true, or, when Jira leaves
    /// <c>isLast</c> out, when <c>startAt</c> plus the number of values reaches <c>total</c>. With neither, nothing
    /// says the list is complete, and the run stops.
    /// </summary>
    private static void RequireCompleteList(JsonElement page, string what)
    {
        var isLast = Optional(page, "isLast");
        if (isLast?.ValueKind == JsonValueKind.True)
        {
            return;
        }

        if (isLast?.ValueKind == JsonValueKind.False)
        {
            throw new UserFacingException($"Jira's {what} has more than one page, and paging is not supported yet.");
        }

        if (Optional(page, "total") is not { } total)
        {
            throw new UserFacingException($"Jira's {what} does not say whether it is complete, so sprints could be missing. Paging is not supported yet.");
        }

        var seen = (Optional(page, "startAt")?.GetInt64() ?? 0) + page.GetProperty("values").GetArrayLength();
        if (seen < total.GetInt64())
        {
            throw new UserFacingException($"Jira's {what} has more than one page, and paging is not supported yet.");
        }
    }

    /// <summary>For token paging. A last page has no <c>nextPageToken</c>, or a null one; both are documented.</summary>
    private static void RequireLastPage(JsonElement page, string what)
    {
        if (Optional(page, "nextPageToken") is not null)
        {
            throw new UserFacingException($"Jira's {what} has more than one page, and paging is not supported yet.");
        }
    }
}
