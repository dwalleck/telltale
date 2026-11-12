using System.Globalization;
using System.Text.Json;
using Telltale.Core;
using Telltale.Core.Secrets;

namespace Telltale.Collectors.Jira;

/// <summary>A workflow status as Jira lists it. <see cref="CategoryKey"/> is Jira's own grouping: new, indeterminate or done.</summary>
public sealed record DiscoveredStatus(string Id, string Name, string CategoryKey);

/// <summary>A resolution as Jira lists it.</summary>
public sealed record DiscoveredResolution(string Id, string Name);

/// <summary>A field as Jira lists it.</summary>
public sealed record DiscoveredField(string Id, string Name);

/// <summary>What <c>discover</c> learns from a Jira site: the IDs the owner would otherwise look up by hand.</summary>
/// <param name="Statuses">Every status of the site's active workflows, in Jira's order.</param>
/// <param name="Resolutions">Every resolution of the site.</param>
/// <param name="SprintFields">The fields of the Sprint type. A site has one.</param>
/// <param name="StoryPointFields">The fields that can hold story points. A site often has two; the board uses one.</param>
/// <param name="ProjectKeys">The keys of the board's projects.</param>
public sealed record JiraSiteFacts(
    IReadOnlyList<DiscoveredStatus> Statuses,
    IReadOnlyList<DiscoveredResolution> Resolutions,
    IReadOnlyList<DiscoveredField> SprintFields,
    IReadOnlyList<DiscoveredField> StoryPointFields,
    IReadOnlyList<string> ProjectKeys);

/// <summary>
/// Reads from one Jira site what the configuration file needs: statuses, resolutions, the Sprint and story
/// point fields, and the board's projects. Only IDs, names of statuses, resolutions and fields, and
/// category keys are kept; descriptions and everything about people are never read.
/// </summary>
public sealed class JiraDiscovery
{
    // The custom field types by which Jira identifies these fields. Field names are editable, so the Sprint
    // field is never found by name.
    private const string SprintFieldType = "com.pyxis.greenhopper.jira:gh-sprint";
    private const string StoryPointEstimateFieldType = "com.pyxis.greenhopper.jira:jsw-story-points";

    // "Story Points" on a company-managed project is an ordinary number field: only its name tells it apart
    // from any other number, so a renamed one is not found and the owner names it.
    private const string StoryPointsFieldName = "Story Points";

    private const int PageSize = 50;

    private readonly HttpMessageHandler _handler;
    private readonly Uri _siteUrl;
    private readonly long _boardId;
    private readonly ISecretStore _secrets;

    public JiraDiscovery(HttpMessageHandler handler, Uri siteUrl, long boardId, ISecretStore secrets)
    {
        _handler = handler;
        _siteUrl = siteUrl;
        _boardId = boardId;
        _secrets = secrets;
    }

    /// <summary>
    /// Reads the site. The status list is asked for first and alone: it is the check that the proxy lets the
    /// request through and that the certificate is trusted, and a failure there says so (see <see cref="JiraClient"/>).
    /// </summary>
    public async Task<JiraSiteFacts> ReadAsync(CancellationToken cancellationToken = default)
    {
        using var jira = new JiraClient(_handler, _siteUrl, _secrets.Require(SecretName.JiraEmail), _secrets.Require(SecretName.JiraApiToken));

        var statuses = await ReadStatusesAsync(jira, cancellationToken);
        var resolutions = await ReadResolutionsAsync(jira, cancellationToken);
        var (sprintFields, storyPointFields) = await ReadFieldsAsync(jira, cancellationToken);
        var projectKeys = await ReadProjectKeysAsync(jira, cancellationToken);

        return new JiraSiteFacts(statuses, resolutions, sprintFields, storyPointFields, projectKeys);
    }

    private static async Task<List<DiscoveredStatus>> ReadStatusesAsync(JiraClient jira, CancellationToken cancellationToken)
    {
        using var document = await jira.GetAsync("rest/api/3/status", cancellationToken);
        return Read<List<DiscoveredStatus>>("status list", () => [.. document.RootElement.EnumerateArray().Select(status => new DiscoveredStatus(
            Text(status, "id"),
            Text(status, "name"),
            Text(status.GetProperty("statusCategory"), "key")))]);
    }

    private static Task<List<DiscoveredResolution>> ReadResolutionsAsync(JiraClient jira, CancellationToken cancellationToken) =>
        ReadAllPagesAsync(jira, "rest/api/3/resolution/search", "resolution list",
            resolution => new DiscoveredResolution(Text(resolution, "id"), Text(resolution, "name")), cancellationToken);

    private static async Task<(List<DiscoveredField> Sprint, List<DiscoveredField> StoryPoints)> ReadFieldsAsync(JiraClient jira, CancellationToken cancellationToken)
    {
        using var document = await jira.GetAsync("rest/api/3/field", cancellationToken);
        return Read("field list", () =>
        {
            var sprint = new List<DiscoveredField>();
            var storyPoints = new List<DiscoveredField>();

            foreach (var field in document.RootElement.EnumerateArray())
            {
                // System fields have no custom type, and some fields have no schema at all.
                if (Optional(field, "schema") is not { } schema || Optional(schema, "custom")?.GetString() is not { } type)
                {
                    continue;
                }

                var found = new DiscoveredField(Text(field, "id"), Text(field, "name"));
                if (type == SprintFieldType)
                {
                    sprint.Add(found);
                }
                else if (type == StoryPointEstimateFieldType
                    || (Optional(schema, "type")?.GetString() == "number" && string.Equals(found.Name, StoryPointsFieldName, StringComparison.OrdinalIgnoreCase)))
                {
                    storyPoints.Add(found);
                }
            }

            return (sprint, storyPoints);
        });
    }

    private Task<List<string>> ReadProjectKeysAsync(JiraClient jira, CancellationToken cancellationToken) =>
        ReadAllPagesAsync(jira, $"rest/agile/1.0/board/{_boardId.ToString(CultureInfo.InvariantCulture)}/project", "list of the board's projects",
            project => Text(project, "key"), cancellationToken);

    /// <summary>
    /// Reads every page of a list paged by offset. Jira marks the last page with <c>isLast</c>; where it
    /// leaves that out, the list ends at the first empty page.
    /// </summary>
    private static async Task<List<T>> ReadAllPagesAsync<T>(JiraClient jira, string path, string what, Func<JsonElement, T> parse, CancellationToken cancellationToken)
    {
        var all = new List<T>();
        while (true)
        {
            using var document = await jira.GetAsync($"{path}?startAt={all.Count.ToString(CultureInfo.InvariantCulture)}&maxResults={PageSize.ToString(CultureInfo.InvariantCulture)}", cancellationToken);
            var (values, isLast) = Read(what, () =>
            {
                var page = document.RootElement;
                return (page.GetProperty("values").EnumerateArray().Select(parse).ToList(), Optional(page, "isLast")?.ValueKind == JsonValueKind.True);
            });

            all.AddRange(values);
            if (isLast || values.Count == 0)
            {
                return all;
            }
        }
    }

    /// <summary>
    /// Reads Jira's answer. An answer in a shape the tool does not know stops the command with a plain
    /// message that names only the kind of failure, never a value from the answer.
    /// </summary>
    private static T Read<T>(string what, Func<T> parse)
    {
        try
        {
            return parse();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new UserFacingException($"Jira's {what} answered in a shape this tool does not understand ({exception.GetType().Name}). Nothing was written.", exception);
        }
    }

    /// <summary>A text value Jira must give. Absent, null or not text is an answer in an unknown shape.</summary>
    private static string Text(JsonElement parent, string name) =>
        parent.GetProperty(name).GetString() ?? throw new FormatException($"{name} is null.");

    /// <summary>A property's value, or null when it is absent or JSON null.</summary>
    private static JsonElement? Optional(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;
}
