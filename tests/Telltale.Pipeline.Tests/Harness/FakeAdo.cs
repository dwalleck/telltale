using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Telltale.Pipeline.Tests.Harness;

/// <summary>A synthetic Azure DevOps identity. Its ID and names may never reach the database, a report or a log.</summary>
public sealed record FakeAdoIdentity(string Id, string DisplayName, string UniqueName);

/// <summary>Synthetic Azure DevOps identities: invented people, a scanner account that only ever acts as a bot, and the service identity that writes system threads.</summary>
public static class AdoIdentities
{
    public static readonly FakeAdoIdentity AuthorOne = new("a0000001-0000-4000-8000-00000000f001", "Fake Ado Author One", "fake.ado.author.one@example.invalid");
    public static readonly FakeAdoIdentity AuthorTwo = new("a0000002-0000-4000-8000-00000000f002", "Fake Ado Author Two", "fake.ado.author.two@example.invalid");
    public static readonly FakeAdoIdentity AuthorThree = new("a0000003-0000-4000-8000-00000000f003", "Fake Ado Author Three", "fake.ado.author.three@example.invalid");
    public static readonly FakeAdoIdentity AuthorFour = new("a0000004-0000-4000-8000-00000000f004", "Fake Ado Author Four", "fake.ado.author.four@example.invalid");
    public static readonly FakeAdoIdentity AuthorFive = new("a0000005-0000-4000-8000-00000000f005", "Fake Ado Author Five", "fake.ado.author.five@example.invalid");
    public static readonly FakeAdoIdentity Reviewer = new("a0000011-0000-4000-8000-00000000f011", "Fake Ado Reviewer One", "fake.ado.reviewer.one@example.invalid");

    /// <summary>The account the configuration designates as automation, by its ID.</summary>
    public static readonly FakeAdoIdentity Scanner = new("a0000031-0000-4000-8000-00000000f031", "Fake Ado Scanner", "fake.ado.scanner@example.invalid");

    /// <summary>The identity system threads are written under when the fixture does not say otherwise.</summary>
    public static readonly FakeAdoIdentity Service = new("a0000041-0000-4000-8000-00000000f041", "Fake Ado Service", "fake.ado.service@example.invalid");

    public static IReadOnlyList<FakeAdoIdentity> Authors { get; } = [AuthorOne, AuthorTwo, AuthorThree, AuthorFour, AuthorFive];

    public static IReadOnlyList<FakeAdoIdentity> All { get; } = [.. Authors, Reviewer, Scanner, Service];
}

/// <summary>One comment of a synthetic thread. <see cref="Type"/> is Azure DevOps's comment type: <c>text</c> or <c>system</c>.</summary>
public sealed record FakeAdoComment(int Id, FakeAdoIdentity? By, DateTimeOffset At, string Type, string Content, bool Deleted = false);

/// <summary>
/// One thread of a synthetic pull request: comments on a file (<see cref="FilePath"/> set), general
/// conversation (neither set), or a system thread that records a vote (<see cref="Vote"/> set, with
/// <see cref="VotedBy"/> the reviewer who voted).
/// </summary>
public sealed record FakeAdoThread(int Id, DateTimeOffset At, string? FilePath, IReadOnlyList<FakeAdoComment> Comments, string? Vote = null, FakeAdoIdentity? VotedBy = null);

/// <summary>A synthetic Azure DevOps pull request, built fluently. Its title, branch, description, file paths and comment texts may never be stored.</summary>
public sealed class FakeAdoPullRequest(FakeAdo ado, int number, FakeAdoIdentity? author, DateTimeOffset opened)
{
    public int Number => number;

    public FakeAdoIdentity? Author => author;

    public DateTimeOffset Opened => opened;

    /// <summary>Azure DevOps's status: <c>active</c>, <c>completed</c> or <c>abandoned</c>.</summary>
    public string Status { get; private set; } = "active";

    public DateTimeOffset? ClosedAt { get; private set; }

    public string Title => $"Synthetic title of Azure DevOps pull request {number} that must never be stored";

    public string Branch => $"refs/heads/fake-ado-branch-{number}-that-must-never-be-stored";

    public string FilePath => $"/src/fake-ado-file-{number}-that-must-never-be-stored.cs";

    public List<FakeAdoThread> Threads { get; } = [];

    /// <summary>The reviewer records of the pull request itself: each reviewer's current vote, which has no time.</summary>
    public Dictionary<FakeAdoIdentity, int> CurrentVotes { get; } = [];

    /// <summary>Completed: Azure DevOps's word for merged.</summary>
    public FakeAdoPullRequest Completed(DateTimeOffset at)
    {
        (Status, ClosedAt) = ("completed", at);
        return this;
    }

    /// <summary>Abandoned: closed without a merge.</summary>
    public FakeAdoPullRequest Abandoned(DateTimeOffset at)
    {
        (Status, ClosedAt) = ("abandoned", at);
        return this;
    }

    /// <summary>A text comment on a file, in a thread of its own.</summary>
    public FakeAdoPullRequest FileComment(FakeAdoIdentity? by, DateTimeOffset at, bool deleted = false)
    {
        var id = ado.NextThreadId();
        Threads.Add(new FakeAdoThread(id, at, FilePath, [new FakeAdoComment(1, by, at, "text", Content(id), deleted)]));
        return this;
    }

    /// <summary>A text comment on the pull request as a whole: general conversation, on no file.</summary>
    public FakeAdoPullRequest Comment(FakeAdoIdentity by, DateTimeOffset at)
    {
        var id = ado.NextThreadId();
        Threads.Add(new FakeAdoThread(id, at, null, [new FakeAdoComment(1, by, at, "text", Content(id))]));
        return this;
    }

    /// <summary>
    /// A vote: 10 approved, 5 approved with suggestions, 0 reset, -5 waiting for the author, -10 rejected. It is
    /// a system thread that names the voter apart from the thread's own author (<paramref name="threadWrittenBy"/>,
    /// the service identity unless given), and it becomes the reviewer's current vote.
    /// </summary>
    public FakeAdoPullRequest Voted(FakeAdoIdentity? by, int vote, DateTimeOffset at, FakeAdoIdentity? threadWrittenBy = null, string? result = null)
    {
        var id = ado.NextThreadId();
        Threads.Add(new FakeAdoThread(
            id, at, null,
            [new FakeAdoComment(1, threadWrittenBy ?? AdoIdentities.Service, at, "system", Content(id))],
            result ?? vote.ToString(CultureInfo.InvariantCulture),
            by));
        if (by is not null)
        {
            CurrentVotes[by] = vote;
        }

        return this;
    }

    /// <summary>A reviewer's current vote with no thread that says when it was cast.</summary>
    public FakeAdoPullRequest CurrentVote(FakeAdoIdentity by, int vote)
    {
        CurrentVotes[by] = vote;
        return this;
    }

    private static string Content(int threadId) => $"Synthetic text of Azure DevOps thread {threadId} that must never be stored";
}

/// <summary>
/// A synthetic Azure DevOps behind the harness's HTTP stub: one repository's pull requests and their threads,
/// served as the Git REST API serves them. The list is paged by <c>$top</c> and <c>$skip</c>, newest first;
/// the threads of a pull request come in one answer, or, with <see cref="ThreadsPerAnswer"/>, in several
/// joined by an <c>x-ms-continuationtoken</c> header. Nothing here names a real organization, project,
/// repository or person. Make one with <see cref="For"/>, which also writes the <c>ado</c> setting.
/// </summary>
public sealed class FakeAdo
{
    /// <summary>The address in the configuration: a name that cannot resolve.</summary>
    public const string ApiUrl = "https://ado.example.invalid/";

    public const string Organization = "fake-ado-org";

    /// <summary>A project name with a space in it, as Azure DevOps allows.</summary>
    public const string Repository = "Fake Project/fake-ado-sample";

    /// <summary>The repository as the store names it.</summary>
    public const string RepositoryKey = "fake project/fake-ado-sample";

    private const string Prefix = "/" + Organization + "/";
    private const string RepositoryPath = Prefix + "Fake%20Project/_apis/git/repositories/fake-ado-sample";

    /// <summary>The path of the list of pull requests.</summary>
    public const string ListPath = RepositoryPath + "/pullrequests";

    private readonly StubHttpHandler _handler;
    private int _threadId = 4400;

    private FakeAdo(StubHttpHandler handler)
    {
        _handler = handler;
        handler.On(HttpMethod.Get, ListPath, request =>
        {
            var skip = int.Parse(request.Query.GetValueOrDefault("$skip", "0"), CultureInfo.InvariantCulture);
            var top = Math.Min(PageSize, int.Parse(request.Query.GetValueOrDefault("$top", "101"), CultureInfo.InvariantCulture));
            return Json(List(PullRequests.OrderByDescending(pullRequest => pullRequest.Opened).ThenByDescending(pullRequest => pullRequest.Number).Skip(skip).Take(top).Select(PullRequestJson)));
        });
    }

    /// <summary>A synthetic Azure DevOps on the harness's stub, and the <c>ado</c> setting that points the run at it.</summary>
    public static FakeAdo For(PipelineHarness pipeline)
    {
        pipeline.Config["ado"] = new JsonObject
        {
            ["apiUrl"] = ApiUrl,
            ["organization"] = Organization,
            ["repositories"] = new JsonArray(Repository),
            ["automationIdentities"] = new JsonArray(AdoIdentities.Scanner.Id.ToUpperInvariant()),
        };
        return new FakeAdo(pipeline.Jira.Handler);
    }

    public List<FakeAdoPullRequest> PullRequests { get; } = [];

    /// <summary>The most rows the list gives per answer, whatever the request asks for.</summary>
    public int PageSize { get; set; } = 1000;

    /// <summary>When set, the most threads one answer gives: the rest follow under a continuation token.</summary>
    public int? ThreadsPerAnswer { get; set; }

    /// <summary>Every request that reached this host.</summary>
    public IReadOnlyList<StubRequest> Requests => [.. _handler.Requests.Where(request => request.Path.StartsWith(Prefix, StringComparison.Ordinal))];

    /// <summary>The requests for the list of pull requests, by the number of rows each asked to skip.</summary>
    public IReadOnlyList<string> ListRowsSkipped => [.. Requests.Where(request => request.Path == ListPath).Select(request => request.Query.GetValueOrDefault("$skip", "0"))];

    /// <summary>The numbers of the pull requests whose threads were asked for, in the order asked, once for each request.</summary>
    public IReadOnlyList<int> ThreadsAskedFor =>
        [.. Requests.Where(request => request.Path.EndsWith("/threads", StringComparison.Ordinal)).Select(request => int.Parse(request.Path.Split('/')[^2], CultureInfo.InvariantCulture))];

    /// <summary>
    /// Text this host serves that may never be written anywhere: identity IDs and names, titles, branch names,
    /// file paths and comment texts. The scanner's ID is not in it: the configuration file names it, by design.
    /// </summary>
    public IReadOnlyList<string> ForbiddenText =>
    [
        .. AdoIdentities.All.SelectMany(identity => new[] { identity.DisplayName, identity.UniqueName }),
        .. AdoIdentities.All.Where(identity => identity != AdoIdentities.Scanner).Select(identity => identity.Id),
        .. PullRequests.SelectMany(pullRequest => new[] { pullRequest.Title, pullRequest.Branch, pullRequest.FilePath }),
        .. PullRequests.SelectMany(pullRequest => pullRequest.Threads.SelectMany(thread => thread.Comments.Select(comment => comment.Content))),
    ];

    public FakeAdoPullRequest AddPullRequest(int number, FakeAdoIdentity? author, DateTimeOffset opened)
    {
        var pullRequest = new FakeAdoPullRequest(this, number, author, opened);
        PullRequests.Add(pullRequest);
        _handler.On(HttpMethod.Get, $"{RepositoryPath}/pullRequests/{number}/threads", request =>
        {
            var from = int.Parse(request.Query.GetValueOrDefault("continuationToken", "0"), CultureInfo.InvariantCulture);
            var size = ThreadsPerAnswer ?? int.MaxValue;
            var response = Json(List(pullRequest.Threads.Skip(from).Take(size).Select(ThreadJson)));
            if (pullRequest.Threads.Count - from > size)
            {
                response.Headers.TryAddWithoutValidation("x-ms-continuationtoken", (from + size).ToString(CultureInfo.InvariantCulture));
            }

            return response;
        });
        return pullRequest;
    }

    internal int NextThreadId() => ++_threadId;

    private static HttpResponseMessage Json(JsonObject body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    /// <summary>A list as Azure DevOps wraps one: the rows and their number.</summary>
    private static JsonObject List(IEnumerable<JsonObject> rows)
    {
        var value = new JsonArray([.. rows]);
        return new JsonObject { ["count"] = value.Count, ["value"] = value };
    }

    private static JsonObject IdentityJson(FakeAdoIdentity identity) => new()
    {
        ["id"] = identity.Id,
        ["displayName"] = identity.DisplayName,
        ["uniqueName"] = identity.UniqueName,
        ["url"] = $"https://ado.example.invalid/{Organization}/_apis/Identities/{identity.Id}",
    };

    private static JsonObject PullRequestJson(FakeAdoPullRequest pullRequest)
    {
        var json = new JsonObject
        {
            ["pullRequestId"] = pullRequest.Number,
            ["status"] = pullRequest.Status,
            ["createdBy"] = pullRequest.Author is { } author ? IdentityJson(author) : null,
            ["creationDate"] = Time(pullRequest.Opened),
            ["title"] = pullRequest.Title,
            ["description"] = $"Synthetic description of Azure DevOps pull request {pullRequest.Number}",
            ["sourceRefName"] = pullRequest.Branch,
            ["targetRefName"] = "refs/heads/main",
            ["isDraft"] = false,
            ["reviewers"] = new JsonArray([.. pullRequest.CurrentVotes.Select(vote =>
            {
                var reviewer = IdentityJson(vote.Key);
                reviewer["vote"] = vote.Value;
                return reviewer;
            })]),
        };

        // Azure DevOps leaves the closing time out of a pull request that is not closed.
        if (pullRequest.ClosedAt is { } closed)
        {
            json["closedDate"] = Time(closed);
        }

        return json;
    }

    private static JsonObject ThreadJson(FakeAdoThread thread)
    {
        var json = new JsonObject
        {
            ["id"] = thread.Id,
            ["publishedDate"] = Time(thread.At),
            ["lastUpdatedDate"] = Time(thread.At),
            ["isDeleted"] = false,
            ["threadContext"] = thread.FilePath is { } path ? new JsonObject { ["filePath"] = path, ["rightFileStart"] = new JsonObject { ["line"] = 3, ["offset"] = 1 } } : null,
            ["comments"] = new JsonArray([.. thread.Comments.Select(comment => new JsonObject
            {
                ["id"] = comment.Id,
                ["parentCommentId"] = 0,
                ["author"] = comment.By is { } by ? IdentityJson(by) : null,
                ["content"] = comment.Deleted ? null : comment.Content,
                ["publishedDate"] = Time(comment.At),
                ["commentType"] = comment.Type,
                ["isDeleted"] = comment.Deleted,
            })]),
        };

        if (thread.Vote is { } vote)
        {
            // The voter is named by a key into the thread's identities, never by the thread's own author.
            var properties = new JsonObject
            {
                ["CodeReviewThreadType"] = Property("VoteUpdate"),
                ["CodeReviewVoteResult"] = Property(vote),
            };
            if (thread.VotedBy is { } voter)
            {
                properties["CodeReviewVotedByIdentity"] = Property("1");
                json["identities"] = new JsonObject { ["1"] = IdentityJson(voter) };
            }

            json["properties"] = properties;
        }

        return json;
    }

    private static JsonObject Property(string value) => new() { ["$type"] = "System.String", ["$value"] = value };

    /// <summary>A time as Azure DevOps writes it: UTC, with a fraction of a second.</summary>
    private static JsonNode Time(DateTimeOffset value) =>
        JsonValue.Create(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
}
