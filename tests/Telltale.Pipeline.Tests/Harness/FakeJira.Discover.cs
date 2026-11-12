using System.Globalization;
using System.Text.Json.Nodes;

namespace Telltale.Pipeline.Tests.Harness;

/// <summary>A resolution as the site lists it.</summary>
public sealed record FakeResolution(string Id, string Name);

/// <summary>A field as the site lists it. <see cref="SchemaCustom"/> is the custom field type; null for a system field.</summary>
public sealed record FakeField(string Id, string Name, string SchemaType, string? SchemaCustom);

/// <summary>A column of the board and the statuses mapped to it, by ID.</summary>
public sealed record FakeColumn(string Name, List<string> StatusIds);

/// <summary>
/// The endpoints only <c>discover</c> calls: the board's configuration, the resolution list, the field list and the board's projects.
/// The collector must never call the board's projects (owner decision D2); it reads the keys from the configuration.
/// </summary>
public sealed partial class FakeJira
{
    public const string SprintFieldType = "com.pyxis.greenhopper.jira:gh-sprint";
    public const string StoryPointEstimateFieldType = "com.pyxis.greenhopper.jira:jsw-story-points";
    public const string NumberFieldType = "com.atlassian.jira.plugin.system.customfieldtypes:float";

    public static readonly FakeResolution ResolutionDone = new("300001", "Fake Done");
    public static readonly FakeResolution ResolutionWontDo = new("300002", "Fake Won't Do");

    public string BoardConfigurationPath => $"/rest/agile/1.0/board/{BoardId}/configuration";

    /// <summary>The board's columns, left to right, with every status of the workflow mapped. Edit the list to change the board.</summary>
    public List<FakeColumn> BoardColumns { get; } =
    [
        new("Fake To Do", [Workflow.New.Id, Workflow.Approved.Id, Workflow.Committed.Id]),
        new("Fake In Progress", [Workflow.Development.Id, Workflow.Review.Id, Workflow.QE.Id, Workflow.Blocked.Id]),
        new("Fake Done", [Workflow.Done.Id, Workflow.Shipped.Id]),
    ];

    public string BoardProjectPath => $"/rest/agile/1.0/board/{BoardId}/project";

    /// <summary>The site's resolutions. Edit the list to change what the site reports.</summary>
    public List<FakeResolution> Resolutions { get; } = [ResolutionDone, ResolutionWontDo];

    /// <summary>The site's fields: one system field, the Sprint field, the story point field and a number field that is neither.</summary>
    public List<FakeField> Fields { get; } =
    [
        new("status", "Status", "status", null),
        new(SprintFieldId, "Fake Sprint", "array", SprintFieldType),
        new(StoryPointFieldId, "Fake Story point estimate", "number", StoryPointEstimateFieldType),
        new("customfield_10030", "Fake Budget", "number", NumberFieldType),
    ];

    /// <summary>The keys of the board's projects.</summary>
    public List<string> BoardProjectKeys { get; } = [ProjectKey];

    /// <summary>How many values an offset-paged list (resolutions, board projects) puts on one page.</summary>
    public int OffsetPageSize { get; set; } = 50;

    private void AddDiscoverRoutes()
    {
        Handler
            .On(HttpMethod.Get, BoardConfigurationPath, _ => Respond(BoardConfiguration()))
            .On(HttpMethod.Get, "/rest/api/3/resolution/search", request => Respond(Page(request, Resolutions.Select(resolution => (JsonNode)new JsonObject
            {
                ["id"] = resolution.Id,
                ["name"] = resolution.Name,
                ["description"] = ResolutionDescription,
                ["isDefault"] = false,
            }))))
            .On(HttpMethod.Get, "/rest/api/3/field", _ => Respond(new JsonArray(Fields.Select(field => (JsonNode)FieldDetails(field)).ToArray())))
            .On(HttpMethod.Get, BoardProjectPath, request => Respond(Page(request, BoardProjectKeys.Select((key, index) => (JsonNode)new JsonObject
            {
                ["self"] = $"{SiteUrl}rest/api/3/project/{key}",
                ["id"] = (100001 + index).ToString(CultureInfo.InvariantCulture),
                ["key"] = key,
                ["name"] = $"Fake Project {key}",
                ["projectTypeKey"] = "software",
            }))));
    }

    /// <summary>
    /// One page of an offset-paged list, starting at the request's <c>startAt</c>. As the schemas say, the page
    /// carries <c>isLast</c> and <c>total</c>; as the documentation also allows, it carries neither, and the
    /// list ends with an empty page.
    /// </summary>
    private JsonObject Page(StubRequest request, IEnumerable<JsonNode> all)
    {
        var items = all.ToList();
        var startAt = request.Query.TryGetValue("startAt", out var asked) ? int.Parse(asked, CultureInfo.InvariantCulture) : 0;
        var values = items.Skip(startAt).Take(OffsetPageSize).ToArray();

        var page = new JsonObject
        {
            ["maxResults"] = OffsetPageSize,
            ["startAt"] = startAt,
            ["values"] = new JsonArray(values),
        };

        if (Shapes == JiraShapes.AsSchemas)
        {
            page["total"] = items.Count;
            page["isLast"] = startAt + values.Length >= items.Count;
        }

        return page;
    }

    /// <summary>The board's configuration as Atlassian's documentation shows it. Not seen from a real site.</summary>
    private JsonObject BoardConfiguration() => new()
    {
        ["id"] = BoardId,
        ["name"] = "Fake Board",
        ["type"] = "scrum",
        ["self"] = $"{SiteUrl}rest/agile/1.0/board/{BoardId}/configuration",
        ["filter"] = new JsonObject { ["id"] = "700001", ["self"] = $"{SiteUrl}rest/api/3/filter/700001" },
        ["columnConfig"] = new JsonObject
        {
            ["columns"] = new JsonArray(BoardColumns.Select(column => (JsonNode)new JsonObject
            {
                ["name"] = column.Name,
                ["statuses"] = new JsonArray(column.StatusIds.Select(id => (JsonNode)new JsonObject { ["id"] = id, ["self"] = $"{SiteUrl}rest/api/3/status/{id}" }).ToArray()),
            }).ToArray()),
            ["constraintType"] = "issueCount",
        },
        ["estimation"] = new JsonObject
        {
            ["type"] = "field",
            ["field"] = new JsonObject { ["fieldId"] = StoryPointFieldId, ["displayName"] = "Fake Story point estimate" },
        },
        ["ranking"] = new JsonObject { ["rankCustomFieldId"] = 10019 },
    };

    private static JsonObject FieldDetails(FakeField field)
    {
        var schema = new JsonObject { ["type"] = field.SchemaType };
        if (field.SchemaCustom is null)
        {
            schema["system"] = field.Id;
        }
        else
        {
            schema["custom"] = field.SchemaCustom;
            schema["customId"] = long.Parse(field.Id.AsSpan("customfield_".Length), CultureInfo.InvariantCulture);
        }

        return new JsonObject
        {
            ["id"] = field.Id,
            ["key"] = field.Id,
            ["name"] = field.Name,
            ["custom"] = field.SchemaCustom is not null,
            ["orderable"] = true,
            ["navigable"] = true,
            ["searchable"] = true,
            ["clauseNames"] = new JsonArray(field.Name),
            ["schema"] = schema,
        };
    }
}
