using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Telltale.Pipeline.Tests.Harness;

/// <summary>
/// Atlassian's documentation describes some response fields in two ways (research note 05). The fake
/// can answer in either, and the collector must read both.
/// </summary>
public enum JiraShapes
{
    /// <summary>As the schemas say: changelog <c>created</c> is an ISO string; a last page carries <c>"nextPageToken": null</c>.</summary>
    AsSchemas,

    /// <summary>As the examples show: changelog <c>created</c> is epoch milliseconds; a last page has no <c>nextPageToken</c>.</summary>
    AsExamples,
}

/// <summary>One entry in an issue's changelog: a single field changing at one moment.</summary>
public sealed record FakeHistory(string Id, DateTimeOffset At, FakePerson Author, string FieldId, string FieldName, string? From, string? FromText, string? To, string? ToText);

/// <summary>A sprint as the board lists it. <see cref="State"/> is Jira's: future, active or closed. A null <see cref="OriginBoardId"/> leaves the field out.</summary>
public sealed record FakeSprint(long Id, string Name, string State, DateTimeOffset? Start, DateTimeOffset? PlannedEnd, DateTimeOffset? Completed, long? OriginBoardId)
{
    public string Goal => $"Synthetic goal of sprint {Id} that must never be stored";
}

/// <summary>
/// An issue type as Jira describes it on an issue. A null <see cref="HierarchyLevel"/> leaves the field out of the response.
/// Jira's levels: -1 sub-task, 0 standard, 1 epic, higher for what sits above epics (research note 05).
/// </summary>
public sealed record FakeIssueType(string Id, string Name, bool Subtask, int? HierarchyLevel)
{
    public static readonly FakeIssueType Story = new(FakeJira.IssueTypeStoryId, "Fake Story", Subtask: false, HierarchyLevel: 0);
    public static readonly FakeIssueType SubTask = new("800002", "Fake Sub-task", Subtask: true, HierarchyLevel: -1);
    public static readonly FakeIssueType Epic = new("800003", "Fake Epic", Subtask: false, HierarchyLevel: 1);
}

/// <summary>
/// A synthetic issue and its history. Build it with the fluent methods, in time order:
/// <c>jira.AddIssue("FAKE-1", created).AssignedTo(People.One).Moved(at, Workflow.Development).Moved(later, Workflow.Done)</c>.
/// To give later tickets a new kind of history (Sprint field, story points), add a method here that
/// appends a <see cref="FakeHistory"/> through <see cref="AddHistory"/>.
/// </summary>
public sealed class FakeIssue
{
    private readonly FakeJira _jira;
    private readonly List<FakeHistory> _histories = [];

    internal FakeIssue(FakeJira jira, string id, string key, DateTimeOffset created, FakeStatus createdIn)
    {
        _jira = jira;
        Id = id;
        Key = key;
        Created = created;
        Updated = created;
        Status = createdIn;
        Summary = $"Synthetic summary of {key} that must never be stored";
        Description = $"Synthetic description of {key} that must never be stored";
    }

    public string Id { get; }

    public string Key { get; }

    public string Summary { get; }

    public string Description { get; }

    public DateTimeOffset Created { get; }

    public DateTimeOffset Updated { get; private set; }

    /// <summary>The status the issue is in now, after every move so far.</summary>
    public FakeStatus Status { get; private set; }

    /// <summary>A story unless <see cref="OfType"/> says otherwise.</summary>
    public FakeIssueType Type { get; private set; } = FakeIssueType.Story;

    public FakePerson? Assignee { get; private set; }

    public double? StoryPoints { get; private set; }

    public string? ResolutionId { get; private set; }

    /// <summary>The sprints the issue's Sprint field lists now.</summary>
    public IReadOnlyList<long> SprintIds { get; private set; } = [];

    public IReadOnlyList<FakeHistory> Histories => _histories;

    public FakeIssue OfType(FakeIssueType type)
    {
        Type = type;
        return this;
    }

    public FakeIssue AssignedTo(FakePerson person)
    {
        Assignee = person;
        return this;
    }

    /// <summary>Sets the current estimate without recording history.</summary>
    public FakeIssue Estimated(double points)
    {
        StoryPoints = points;
        return this;
    }

    /// <summary>Sets the current resolution, as Jira does when an issue is closed.</summary>
    public FakeIssue Resolved(string resolutionId)
    {
        ResolutionId = resolutionId;
        return this;
    }

    /// <summary>
    /// Puts the issue in <paramref name="status"/> now without recording a transition, so its current status
    /// disagrees with its changelog (a workflow migration, a bulk move, history Jira lost).
    /// </summary>
    public FakeIssue NowIn(FakeStatus status)
    {
        Status = status;
        return this;
    }

    /// <summary>Moves the issue from its current status to <paramref name="to"/> and records the transition, made by <paramref name="by"/> (the board admin by default).</summary>
    public FakeIssue Moved(DateTimeOffset at, FakeStatus to, FakePerson? by = null)
    {
        AddHistory(at, "status", "status", Status.Id, _jira.NameOf(Status), to.Id, _jira.NameOf(to), by);
        Status = to;
        return this;
    }

    /// <summary>Sets the Sprint field without recording history, as for an issue created inside a sprint.</summary>
    public FakeIssue InSprints(params long[] sprintIds)
    {
        SprintIds = sprintIds;
        return this;
    }

    /// <summary>
    /// Changes the Sprint field from what it lists now to <paramref name="sprintIds"/> and records the change.
    /// No IDs means the issue was taken out of every sprint.
    /// </summary>
    public FakeIssue SprintsChanged(DateTimeOffset at, params long[] sprintIds)
    {
        AddHistory(at, FakeJira.SprintFieldId, "Sprint", SprintList(SprintIds), SprintNames(SprintIds), SprintList(sprintIds), SprintNames(sprintIds));
        SprintIds = sprintIds;
        return this;
    }

    // A Sprint changelog item holds comma-separated sprint IDs, and an empty text for no sprint (research note 05).
    private static string SprintList(IReadOnlyList<long> sprintIds) => string.Join(", ", sprintIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));

    private static string SprintNames(IReadOnlyList<long> sprintIds) => string.Join(", ", sprintIds.Select(FakeJira.SprintName));

    /// <summary>Appends one changelog entry with the next history ID.</summary>
    public FakeIssue AddHistory(DateTimeOffset at, string fieldId, string fieldName, string? from, string? fromText, string? to, string? toText, FakePerson? author = null)
    {
        _histories.Add(new FakeHistory(_jira.NextHistoryId(), at, author ?? People.BoardAdmin, fieldId, fieldName, from, fromText, to, toText));
        Updated = at > Updated ? at : Updated;
        return this;
    }
}

/// <summary>
/// A synthetic Jira Cloud site with one Scrum board, answering the endpoints the collector calls
/// from the state built by the test. Responses are produced at request time, so a test can change
/// the site between two runs. Shapes follow research note 05; nothing here is real Jira data.
/// </summary>
/// <remarks>
/// The one place to add fixtures: <see cref="AddSprint"/>, <see cref="AddIssue"/> and the methods on
/// <see cref="FakeIssue"/>. To serve a new endpoint, add a route in the constructor. To make one
/// request misbehave, use <see cref="StubHttpHandler.Override"/> on <see cref="Handler"/>.
/// Responses always carry names, emails and summaries, asked for or not, so the privacy scan means something.
/// </remarks>
public sealed class FakeJira
{
    public const string SiteUrl = "https://example.invalid/";
    public const long BoardId = 400001;
    public const string ProjectKey = "FAKE";
    public const string SprintFieldId = "customfield_10020";
    public const string StoryPointFieldId = "customfield_10016";
    public const string IssueTypeStoryId = "800001";
    public const string ResolutionDescription = "Synthetic resolution description that must never be stored";
    public const string StatusDescription = "Synthetic status description that must never be stored";

    private readonly Lock _gate = new();
    private readonly List<FakeSprint> _sprints = [];
    private readonly List<FakeIssue> _issues = [];
    private readonly Dictionary<string, string> _statusNames = Workflow.BoardOrder.ToDictionary(status => status.Id, status => status.DefaultName);
    private List<FakePerson>? _servedOncePer;
    private int _nextIssueId = 900001;
    private int _nextHistoryId = 200001;

    public FakeJira()
    {
        Handler = new StubHttpHandler()
            .On(HttpMethod.Get, $"/rest/agile/1.0/board/{BoardId}/sprint", _ => Respond(Sprints()))
            .On(HttpMethod.Get, "/rest/api/3/status", _ => Respond(Statuses()))
            .On(HttpMethod.Post, "/rest/api/3/search/jql", _ => Respond(Search()))
            .On(HttpMethod.Post, "/rest/api/3/changelog/bulkfetch", request => Respond(Changelogs(request)));
    }

    public StubHttpHandler Handler { get; }

    /// <summary>Runs before each of the site's own answers, for example to move the clock as if the fetch took time.</summary>
    public Action? BeforeResponse { get; set; }

    /// <summary>Which of the two documented response shapes to serve.</summary>
    public JiraShapes Shapes { get; set; } = JiraShapes.AsSchemas;

    /// <summary>
    /// Serves every issue once per person from now on: copies alike in everything but ID, key and assignee
    /// (an issue's own <see cref="FakeIssue.AssignedTo"/> is not used). A figure is shown only for a team of
    /// five with three assignees behind the figure (ADR-0003), so a test about any other rule calls this with
    /// <see cref="People.Team"/> and expects every count multiplied by the number of people. Medians and P85
    /// stay what they are for the issues the test added, because every value is repeated equally often.
    /// </summary>
    public void ServeEachIssueOncePer(IReadOnlyList<FakePerson> people)
    {
        lock (_gate)
        {
            _servedOncePer = [.. people];
        }
    }

    /// <summary>The issues as the site serves them: each one as it is, or once per person (<see cref="ServeEachIssueOncePer"/>).</summary>
    private List<ServedIssue> Served()
    {
        lock (_gate)
        {
            return _servedOncePer is null
                ? [.. _issues.Select(issue => new ServedIssue(issue, issue.Id, issue.Key, issue.Assignee, HistorySuffix: ""))]
                : [.. _issues.SelectMany(issue => _servedOncePer.Select((person, copy) => copy == 0
                    ? new ServedIssue(issue, issue.Id, issue.Key, person, HistorySuffix: "")
                    : new ServedIssue(issue, $"{issue.Id}{copy:00}", $"{ProjectKey}-{issue.Id}{copy:00}", person, $"{copy:00}")))];
        }
    }

    /// <summary>One issue of a response: a <see cref="FakeIssue"/> under the ID, key and assignee it is served with.</summary>
    private sealed record ServedIssue(FakeIssue Issue, string Id, string Key, FakePerson? Assignee, string HistorySuffix);

    public IReadOnlyList<FakeIssue> Issues
    {
        get
        {
            lock (_gate)
            {
                return [.. _issues];
            }
        }
    }

    /// <summary>
    /// Every piece of personal or free text the site can serve: account IDs, display names and emails
    /// (assignees and changelog authors alike), issue summaries and descriptions, sprint goals, and the
    /// resolution and status descriptions. None of it may appear in the database,
    /// a report or the console output.
    /// </summary>
    public IReadOnlyList<string> ForbiddenText =>
    [
        .. People.All.SelectMany(person => new[] { person.AccountId, person.DisplayName, person.Email }),
        .. Issues.SelectMany(issue => new[] { issue.Summary, issue.Description }),
        .. SprintList().Select(sprint => sprint.Goal),
        ResolutionDescription,
        StatusDescription,
    ];

    /// <summary>
    /// Adds a sprint that has started. With <paramref name="completed"/> it is closed; without, it is the active sprint.
    /// <paramref name="originBoardId"/> is the board the sprint was created on; null leaves the field out of the response.
    /// </summary>
    public FakeSprint AddSprint(long id, string name, DateTimeOffset start, DateTimeOffset plannedEnd, DateTimeOffset? completed = null, long? originBoardId = BoardId) =>
        Add(new FakeSprint(id, name, completed is null ? "active" : "closed", start, plannedEnd, completed, originBoardId));

    /// <summary>Adds a closed sprint for which Jira gives no completion time, and a planned end only if one is passed.</summary>
    public FakeSprint AddClosedSprintWithoutCompletion(long id, string name, DateTimeOffset start, DateTimeOffset? plannedEnd = null) =>
        Add(new FakeSprint(id, name, "closed", start, plannedEnd, Completed: null, BoardId));

    /// <summary>Adds a sprint that has not started. Jira lets a future sprint carry planned dates, or none.</summary>
    public FakeSprint AddFutureSprint(long id, string name, DateTimeOffset? plannedStart = null, DateTimeOffset? plannedEnd = null) =>
        Add(new FakeSprint(id, name, "future", plannedStart, plannedEnd, Completed: null, BoardId));

    private FakeSprint Add(FakeSprint sprint)
    {
        lock (_gate)
        {
            _sprints.Add(sprint);
        }

        return sprint;
    }

    private List<FakeSprint> SprintList()
    {
        lock (_gate)
        {
            return [.. _sprints];
        }
    }

    /// <summary>Adds an unassigned issue created in <paramref name="createdIn"/> (New by default). Chain <see cref="FakeIssue"/> methods to give it history.</summary>
    public FakeIssue AddIssue(string key, DateTimeOffset created, FakeStatus? createdIn = null)
    {
        lock (_gate)
        {
            var issue = new FakeIssue(this, (_nextIssueId++).ToString(CultureInfo.InvariantCulture), key, created, createdIn ?? Workflow.New);
            _issues.Add(issue);
            return issue;
        }
    }

    /// <summary>
    /// Gives the sprints a team of five without delivering anything: one issue per person of
    /// <see cref="People.Team"/>, listed in the sprints since 2 January 2026 and left in New. A delivered count is
    /// shown only for a sprint with five distinct assignees (ADR-0003), so a test about delivered counts calls
    /// this and keeps its own counts. The five issues do show in New in the sprints' time in status.
    /// </summary>
    public void AddIdleTeam(params long[] sprintIds)
    {
        foreach (var (person, number) in People.Team.Select((person, index) => (person, index + 1)))
        {
            AddIssue($"{ProjectKey}-{9000 + number}", TeamTime.Local(2026, 1, 2, 9)).AssignedTo(person).InSprints(sprintIds);
        }
    }

    /// <summary>Renames a status everywhere the site reports it. Its ID stays the same.</summary>
    public void RenameStatus(FakeStatus status, string name)
    {
        lock (_gate)
        {
            _statusNames[status.Id] = name;
        }
    }

    internal string NameOf(FakeStatus status)
    {
        lock (_gate)
        {
            return _statusNames[status.Id];
        }
    }

    internal string NextHistoryId()
    {
        lock (_gate)
        {
            return (_nextHistoryId++).ToString(CultureInfo.InvariantCulture);
        }
    }

    private HttpResponseMessage Respond(JsonNode body)
    {
        BeforeResponse?.Invoke();
        return new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
    }

    private JsonObject Sprints()
    {
        var sprints = SprintList();

        var values = new JsonArray();
        foreach (var sprint in sprints)
        {
            var value = new JsonObject
            {
                ["id"] = sprint.Id,
                ["self"] = $"{SiteUrl}rest/agile/1.0/sprint/{sprint.Id}",
                ["state"] = sprint.State,
                ["name"] = sprint.Name,
                ["goal"] = sprint.Goal,
            };

            // Jira leaves unset fields out of a sprint instead of sending null.
            if (sprint.Start is { } start)
            {
                value["startDate"] = AgileTime(start);
            }

            if (sprint.PlannedEnd is { } plannedEnd)
            {
                value["endDate"] = AgileTime(plannedEnd);
            }

            if (sprint.Completed is { } completed)
            {
                value["completeDate"] = AgileTime(completed);
            }

            if (sprint.OriginBoardId is { } originBoardId)
            {
                value["originBoardId"] = originBoardId;
            }

            values.Add(value);
        }

        return new JsonObject
        {
            ["maxResults"] = 50,
            ["startAt"] = 0,
            ["total"] = sprints.Count,
            ["isLast"] = true,
            ["values"] = values,
        };
    }

    private JsonArray Statuses() => new(Workflow.BoardOrder.Select(status => (JsonNode)StatusDetails(status)).ToArray());

    private JsonObject Search()
    {
        var issues = new JsonArray();
        foreach (var served in Served().OrderBy(served => served.Issue.Updated))
        {
            var issue = served.Issue;
            issues.Add(new JsonObject
            {
                ["expand"] = "",
                ["id"] = served.Id,
                ["key"] = served.Key,
                ["self"] = $"{SiteUrl}rest/api/3/issue/{served.Id}",
                ["fields"] = new JsonObject
                {
                    ["summary"] = issue.Summary,
                    ["description"] = issue.Description,
                    ["issuetype"] = IssueTypeDetails(issue.Type),
                    ["status"] = StatusDetails(issue.Status),
                    ["resolution"] = issue.ResolutionId is null ? null : new JsonObject { ["id"] = issue.ResolutionId, ["name"] = "Fake Resolution", ["description"] = ResolutionDescription },
                    ["assignee"] = served.Assignee is null ? null : UserDetails(served.Assignee),
                    ["created"] = PlatformTime(issue.Created),
                    ["updated"] = PlatformTime(issue.Updated),
                    [SprintFieldId] = SprintField(issue),
                    [StoryPointFieldId] = issue.StoryPoints,
                },
            });
        }

        var page = new JsonObject { ["issues"] = issues, ["isLast"] = true };
        AddLastPageToken(page);
        return page;
    }

    /// <summary>The Sprint field of a search result: null when the issue is in no sprint, otherwise one object per sprint.</summary>
    private JsonArray? SprintField(FakeIssue issue)
    {
        if (issue.SprintIds.Count == 0)
        {
            return null;
        }

        var known = SprintList().ToDictionary(sprint => sprint.Id);
        return new JsonArray([.. issue.SprintIds.Select(id =>
        {
            // The field's shape differs from the board's sprint list: boardId, not originBoardId.
            var value = new JsonObject { ["id"] = id, ["name"] = SprintName(id), ["state"] = "closed", ["boardId"] = BoardId };
            if (known.TryGetValue(id, out var sprint))
            {
                value["name"] = sprint.Name;
                value["state"] = sprint.State;
                value["goal"] = sprint.Goal;
            }

            return (JsonNode)value;
        })]);
    }

    /// <summary>The name the fake gives a sprint in changelog text.</summary>
    internal static string SprintName(long id) => $"Fake Sprint {id}";

    private JsonObject Changelogs(StubRequest request)
    {
        using var body = JsonDocument.Parse(request.Body);
        var asked = body.RootElement.GetProperty("issueIdsOrKeys").EnumerateArray().Select(id => id.GetString()).ToHashSet();
        var fieldIds = body.RootElement.TryGetProperty("fieldIds", out var fields)
            ? fields.EnumerateArray().Select(id => id.GetString()).ToHashSet()
            : null;

        var logs = new JsonArray();
        foreach (var served in Served().Where(served => asked.Contains(served.Id) || asked.Contains(served.Key)))
        {
            var histories = new JsonArray();
            foreach (var history in served.Issue.Histories.Where(history => fieldIds is null || fieldIds.Contains(history.FieldId)))
            {
                histories.Add(new JsonObject
                {
                    ["id"] = history.Id + served.HistorySuffix,
                    ["author"] = UserDetails(history.Author),
                    ["created"] = Shapes == JiraShapes.AsExamples ? history.At.ToUnixTimeMilliseconds() : PlatformTime(history.At),
                    ["items"] = new JsonArray(new JsonObject
                    {
                        ["field"] = history.FieldName,
                        ["fieldtype"] = history.FieldId.StartsWith("customfield_", StringComparison.Ordinal) ? "custom" : "jira",
                        ["fieldId"] = history.FieldId,
                        ["from"] = history.From,
                        ["fromString"] = history.FromText,
                        ["to"] = history.To,
                        ["toString"] = history.ToText,
                    }),
                });
            }

            if (histories.Count > 0)
            {
                logs.Add(new JsonObject { ["issueId"] = served.Id, ["changeHistories"] = histories });
            }
        }

        var page = new JsonObject { ["issueChangeLogs"] = logs };
        AddLastPageToken(page);
        return page;
    }

    private void AddLastPageToken(JsonObject page)
    {
        if (Shapes == JiraShapes.AsSchemas)
        {
            page["nextPageToken"] = null;
        }
    }

    private static JsonObject IssueTypeDetails(FakeIssueType type)
    {
        var details = new JsonObject { ["id"] = type.Id, ["name"] = type.Name, ["subtask"] = type.Subtask };
        if (type.HierarchyLevel is { } level)
        {
            details["hierarchyLevel"] = level;
        }

        return details;
    }

    private JsonObject StatusDetails(FakeStatus status) => new()
    {
        ["self"] = $"{SiteUrl}rest/api/3/status/{status.Id}",
        ["id"] = status.Id,
        ["name"] = NameOf(status),
        ["description"] = StatusDescription,
        ["statusCategory"] = new JsonObject { ["id"] = status.CategoryKey switch { "new" => 2, "done" => 3, _ => 4 }, ["key"] = status.CategoryKey, ["name"] = status.CategoryKey },
    };

    private static JsonObject UserDetails(FakePerson person) => new()
    {
        ["self"] = $"{SiteUrl}rest/api/3/user?accountId={person.AccountId}",
        ["accountId"] = person.AccountId,
        ["accountType"] = "atlassian",
        ["active"] = true,
        ["displayName"] = person.DisplayName,
        ["emailAddress"] = person.Email,
        ["timeZone"] = "Etc/UTC",
    };

    /// <summary>The platform API's documented style: milliseconds and an offset without a colon.</summary>
    private static string PlatformTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture) + "+0000";

    /// <summary>The Agile API's style on real sites: UTC with a Z suffix.</summary>
    private static string AgileTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
