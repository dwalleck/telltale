using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Telltale.Core;
using Telltale.Core.Configuration;
using Telltale.Core.Identity;
using Telltale.Core.Secrets;
using Telltale.Core.Storage;

namespace Telltale.Collectors.Jira;

/// <summary>
/// Brings the store up to date with the configured Jira board: its sprints and statuses, the issues
/// of the board's projects, and each issue's status history from the bulk changelog endpoint
/// (ADR-0004). Only selected fields are kept (ADR-0002), and the assignee's account ID is hashed
/// before anything leaves this class (ADR-0003).
/// </summary>
public sealed class JiraCollector
{
    // The bulk changelog endpoint takes at most 1000 issues per request.
    private const int ChangelogIssuesPerRequest = 1000;

    private readonly HttpMessageHandler _handler;
    private readonly JiraConfig _config;
    private readonly ISecretStore _secrets;

    public JiraCollector(HttpMessageHandler handler, JiraConfig config, ISecretStore secrets)
    {
        _handler = handler;
        _config = config;
        _secrets = secrets;
    }

    public async Task BringUpToDateAsync(ITelltaleStore store, CancellationToken cancellationToken = default)
    {
        // All three secrets are checked before the first call, so a missing salt cannot surface after a long fetch.
        var salt = _secrets.Require(SecretName.HashingSalt);
        using var jira = new JiraClient(_handler, _config.SiteUrl, _secrets.Require(SecretName.JiraEmail), _secrets.Require(SecretName.JiraApiToken));

        var statuses = await FetchStatusesAsync(jira, cancellationToken);
        var sprints = await FetchSprintsAsync(jira, cancellationToken);
        var projectKeys = await FetchBoardProjectKeysAsync(jira, cancellationToken);
        var issues = await FetchIssuesAsync(jira, projectKeys, salt, cancellationToken);
        var changes = await FetchChangesAsync(jira, issues, cancellationToken);

        await store.WriteRawAsync(new RawData(issues, changes, sprints, statuses), cancellationToken);
    }

    private static async Task<List<RawStatus>> FetchStatusesAsync(JiraClient jira, CancellationToken cancellationToken)
    {
        using var document = await jira.GetAsync("rest/api/3/status", cancellationToken);
        return [.. document.RootElement.EnumerateArray().Select(status => new RawStatus(
            status.GetProperty("id").GetString()!,
            status.GetProperty("name").GetString()!,
            status.GetProperty("statusCategory").GetProperty("key").GetString()!))];
    }

    private async Task<List<RawSprint>> FetchSprintsAsync(JiraClient jira, CancellationToken cancellationToken)
    {
        using var document = await jira.GetAsync($"rest/agile/1.0/board/{_config.BoardId}/sprint", cancellationToken);
        RequireSinglePage(document.RootElement, "sprint list");

        return [.. document.RootElement.GetProperty("values").EnumerateArray().Select(sprint => new RawSprint(
            sprint.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture),
            _config.BoardId,
            sprint.GetProperty("name").GetString()!,
            sprint.GetProperty("state").GetString()!,
            OptionalTime(sprint, "startDate"),
            OptionalTime(sprint, "endDate"),
            OptionalTime(sprint, "completeDate")))];
    }

    private async Task<List<string>> FetchBoardProjectKeysAsync(JiraClient jira, CancellationToken cancellationToken)
    {
        using var document = await jira.GetAsync($"rest/agile/1.0/board/{_config.BoardId}/project", cancellationToken);
        RequireSinglePage(document.RootElement, "list of the board's projects");

        List<string> keys = [.. document.RootElement.GetProperty("values").EnumerateArray().Select(project => project.GetProperty("key").GetString()!)];
        if (keys.Count == 0)
        {
            throw new UserFacingException($"Jira lists no projects for board {_config.BoardId}. Check the board ID and that the account can see the board.");
        }

        return keys;
    }

    private async Task<List<RawIssue>> FetchIssuesAsync(JiraClient jira, List<string> projectKeys, string salt, CancellationToken cancellationToken)
    {
        // A JQL date is read in the calling user's time zone, so the bound is a whole day, not an instant.
        var projects = string.Join(", ", projectKeys.Select(key => $"\"{key}\""));
        var body = new JsonObject
        {
            ["jql"] = $"project in ({projects}) AND updated >= \"{_config.BackfillStart:yyyy-MM-dd}\" ORDER BY updated ASC",

            // Named fields only: the default is the ID alone, and free-text fields are never asked for.
            ["fields"] = new JsonArray("issuetype", "status", "resolution", "assignee", "created", "updated", _config.StoryPointFieldId),
        };

        using var document = await jira.PostAsync("rest/api/3/search/jql", body, cancellationToken);
        RequireLastPage(document.RootElement, "issue search");

        return [.. document.RootElement.GetProperty("issues").EnumerateArray().Select(issue =>
        {
            var fields = issue.GetProperty("fields");
            return new RawIssue(
                issue.GetProperty("id").GetString()!,
                issue.GetProperty("key").GetString()!,
                fields.GetProperty("issuetype").GetProperty("id").GetString()!,
                JiraTime.Parse(fields.GetProperty("created")),
                fields.GetProperty("status").GetProperty("id").GetString()!,
                Optional(fields, "resolution")?.GetProperty("id").GetString(),
                Optional(fields, _config.StoryPointFieldId)?.GetDouble(),
                Optional(fields, "assignee") is { } assignee ? IdentityHash.Of(assignee.GetProperty("accountId").GetString()!, salt) : null,
                JiraTime.Parse(fields.GetProperty("updated")));
        })];
    }

    private static async Task<List<RawChange>> FetchChangesAsync(JiraClient jira, List<RawIssue> issues, CancellationToken cancellationToken)
    {
        var changes = new List<RawChange>();

        foreach (var chunk in issues.Chunk(ChangelogIssuesPerRequest))
        {
            var body = new JsonObject
            {
                ["issueIdsOrKeys"] = new JsonArray([.. chunk.Select(issue => (JsonNode)issue.IssueId)]),
                ["fieldIds"] = new JsonArray("status"),
            };

            using var document = await jira.PostAsync("rest/api/3/changelog/bulkfetch", body, cancellationToken);
            RequireLastPage(document.RootElement, "bulk changelog");

            foreach (var log in document.RootElement.GetProperty("issueChangeLogs").EnumerateArray())
            {
                var issueId = log.GetProperty("issueId").GetString()!;
                foreach (var history in log.GetProperty("changeHistories").EnumerateArray())
                {
                    var historyId = history.GetProperty("id").GetString()!;
                    var at = JiraTime.Parse(history.GetProperty("created"));

                    foreach (var item in history.GetProperty("items").EnumerateArray())
                    {
                        if (Optional(item, "fieldId")?.GetString() == "status")
                        {
                            changes.Add(new RawChange(historyId, issueId, at, ChangeFields.Status, Optional(item, "from")?.GetString(), Optional(item, "to")?.GetString()));
                        }
                    }
                }
            }
        }

        return changes;
    }

    /// <summary>A property's value, or null when it is absent or JSON null. Jira uses both for "not set".</summary>
    private static JsonElement? Optional(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

    private static DateTimeOffset? OptionalTime(JsonElement parent, string name) =>
        Optional(parent, name) is { } value ? JiraTime.Parse(value) : null;

    // Paging is not built yet. Until it is, a second page stops the run instead of being dropped silently.

    /// <summary>For the Agile API's offset paging (<c>isLast</c>).</summary>
    private static void RequireSinglePage(JsonElement page, string what)
    {
        if (page.TryGetProperty("isLast", out var isLast) && isLast.ValueKind == JsonValueKind.False)
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
