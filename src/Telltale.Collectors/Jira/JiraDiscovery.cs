using System.Globalization;
using System.Net;
using System.Text.Json;
using Telltale.Core;
using Telltale.Core.Secrets;
using static Telltale.Collectors.Jira.JiraJson;

namespace Telltale.Collectors.Jira;

/// <summary>A workflow status as Jira lists it. <see cref="CategoryKey"/> is Jira's own grouping: new, indeterminate or done.</summary>
public sealed record DiscoveredStatus(string Id, string Name, string CategoryKey);

/// <summary>A resolution as Jira lists it.</summary>
public sealed record DiscoveredResolution(string Id, string Name);

/// <summary>A field as Jira lists it. <see cref="ValueType"/> is the type of its value (number, array, ...), null where Jira gives none.</summary>
public sealed record DiscoveredField(string Id, string Name, string? ValueType);

/// <summary>What <c>discover</c> learns from a Jira site: the IDs the owner would otherwise look up by hand.</summary>
/// <param name="Statuses">Every status of the site's active workflows: the source of names and categories.</param>
/// <param name="BoardStatusIds">The IDs of the statuses mapped to the board's columns, in column order.</param>
/// <param name="Resolutions">Every resolution of the site.</param>
/// <param name="Fields">Every field the account may see.</param>
/// <param name="SprintFields">The fields of the Sprint type. A site has one.</param>
/// <param name="StoryPointFields">The fields that can hold story points. A site often has two; the board uses one.</param>
/// <param name="ProjectKeys">The keys of the board's projects.</param>
public sealed record JiraSiteFacts(
    IReadOnlyList<DiscoveredStatus> Statuses,
    IReadOnlyList<string> BoardStatusIds,
    IReadOnlyList<DiscoveredResolution> Resolutions,
    IReadOnlyList<DiscoveredField> Fields,
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
    private readonly TimeProvider _clock;
    private readonly TextWriter _notices;

    /// <param name="clock">Times the waits Jira asks for when it limits the rate of requests.</param>
    /// <param name="notices">Where each such wait is announced: standard error.</param>
    public JiraDiscovery(HttpMessageHandler handler, Uri siteUrl, long boardId, ISecretStore secrets, TimeProvider clock, TextWriter notices)
    {
        _clock = clock;
        _notices = notices;
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
        using var jira = new JiraClient(_handler, _siteUrl, _secrets.Require(SecretName.JiraEmail), _secrets.Require(SecretName.JiraApiToken), _clock, _notices);

        var statuses = await ReadStatusesAsync(jira, cancellationToken);
        var boardStatusIds = await ReadBoardStatusIdsAsync(jira, cancellationToken);
        var resolutions = await ReadResolutionsAsync(jira, cancellationToken);
        var (fields, sprintFields, storyPointFields) = await ReadFieldsAsync(jira, cancellationToken);
        var projectKeys = await ReadProjectKeysAsync(jira, cancellationToken);

        return new JiraSiteFacts(statuses, boardStatusIds, resolutions, fields, sprintFields, storyPointFields, projectKeys);
    }

    private async Task<List<DiscoveredStatus>> ReadStatusesAsync(JiraClient jira, CancellationToken cancellationToken)
    {
        using var document = await FirstCallAsync(jira, "rest/api/3/status", cancellationToken);
        return Read<List<DiscoveredStatus>>("status list", () => [.. Statuses(document.RootElement).Select(status => new DiscoveredStatus(status.Id, status.Name, status.CategoryKey))]);
    }

    /// <summary>
    /// The connection check. What its failure most likely means depends on who answered. When Jira itself
    /// refuses with 401 or 403, the request got through, so the Jira email or the API token is wrong. When no
    /// answer of Jira's arrives (no connection, a TLS failure, a timeout, a proxy asking for its own sign-in,
    /// a page served in place of JSON), the proxy or the trust in the corporate root certificate is. Any
    /// other error from Jira is reported as it is.
    /// </summary>
    private async Task<JsonDocument> FirstCallAsync(JiraClient jira, string path, CancellationToken cancellationToken)
    {
        try
        {
            return await jira.GetAsync(path, cancellationToken);
        }
        catch (UserFacingException exception) when (jira.LastRefusal is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new UserFacingException(
                "Jira refused discover's first call. The likely cause is the Jira email or the API token in the secret store. "
                + $"{_secrets.HowToSet(SecretName.JiraEmail)} {_secrets.HowToSet(SecretName.JiraApiToken)} The failure was: {exception.Message}",
                exception);
        }
        catch (UserFacingException exception) when (jira.LastRefusal is null)
        {
            throw new UserFacingException(
                "discover's first call to Jira failed. When this call fails, the likely cause is the system proxy or trust in the corporate root certificate. "
                + $"The failure was: {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// The statuses mapped to the board's columns, in column order (owner decision D29). A status the board
    /// maps to no column is not on the board. The shape is from Atlassian's documentation and has not been
    /// seen from a real site, so anything else stops the command.
    /// </summary>
    private async Task<List<string>> ReadBoardStatusIdsAsync(JiraClient jira, CancellationToken cancellationToken)
    {
        using var document = await jira.GetAsync($"rest/agile/1.0/board/{_boardId.ToString(CultureInfo.InvariantCulture)}/configuration", cancellationToken);
        return Read<List<string>>("board configuration", () => [.. document.RootElement.GetProperty("columnConfig").GetProperty("columns").EnumerateArray()
            .SelectMany(column => column.GetProperty("statuses").EnumerateArray())
            .Select(status => Text(status, "id"))
            .Distinct(StringComparer.Ordinal)]);
    }

    private static Task<List<DiscoveredResolution>> ReadResolutionsAsync(JiraClient jira, CancellationToken cancellationToken) =>
        ReadAllPagesAsync(jira, "rest/api/3/resolution/search", "resolution list",
            resolution => new DiscoveredResolution(Text(resolution, "id"), Text(resolution, "name")), resolution => resolution.Id, cancellationToken);

    private static async Task<(List<DiscoveredField> All, List<DiscoveredField> Sprint, List<DiscoveredField> StoryPoints)> ReadFieldsAsync(JiraClient jira, CancellationToken cancellationToken)
    {
        using var document = await jira.GetAsync("rest/api/3/field", cancellationToken);
        return Read("field list", () =>
        {
            var all = new List<DiscoveredField>();
            var sprint = new List<DiscoveredField>();
            var storyPoints = new List<DiscoveredField>();

            foreach (var field in document.RootElement.EnumerateArray())
            {
                // Some fields have no schema at all, and system fields have no custom type.
                var schema = Optional(field, "schema");
                var found = new DiscoveredField(Text(field, "id"), Text(field, "name"), schema is { } typed ? Optional(typed, "type")?.GetString() : null);
                all.Add(found);

                var type = schema is { } custom ? Optional(custom, "custom")?.GetString() : null;
                if (type == SprintFieldType)
                {
                    sprint.Add(found);
                }
                else if (type == StoryPointEstimateFieldType
                    || (type is not null && found.ValueType == "number" && string.Equals(found.Name, StoryPointsFieldName, StringComparison.OrdinalIgnoreCase)))
                {
                    storyPoints.Add(found);
                }
            }

            return (all, sprint, storyPoints);
        });
    }

    private Task<List<string>> ReadProjectKeysAsync(JiraClient jira, CancellationToken cancellationToken) =>
        ReadAllPagesAsync(jira, $"rest/agile/1.0/board/{_boardId.ToString(CultureInfo.InvariantCulture)}/project", "list of the board's projects",
            project => Text(project, "key"), key => key, cancellationToken);

    /// <summary>Reads every page of a list paged by offset, ending at the first page that brings nothing new (see <see cref="OffsetPages"/>).</summary>
    private static Task<List<T>> ReadAllPagesAsync<T>(
        JiraClient jira, string path, string what, Func<JsonElement, T> parse, Func<T, string> keyOf, CancellationToken cancellationToken) =>
        OffsetPages.ReadAllAsync(jira, path, PageSize, OffsetPaging.EndsWhenNothingNew, what, "Nothing was written.", page => Read(what, page), parse, keyOf, cancellationToken);

    /// <summary>Reads Jira's answer (<see cref="JiraJson.Read"/>); when its shape is not known, nothing was written.</summary>
    private static T Read<T>(string what, Func<T> parse) => JiraJson.Read(what, "Nothing was written.", parse);
}
