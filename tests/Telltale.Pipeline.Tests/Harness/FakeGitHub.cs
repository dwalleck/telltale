using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Telltale.Pipeline.Tests.Harness;

/// <summary>A synthetic GitHub account. Its ID and login may never reach the database, a report or a log.</summary>
public sealed record FakeAccount(long Id, string Login, string Type = "User");

/// <summary>Synthetic GitHub accounts: invented people, an Ops member and a bot.</summary>
public static class Accounts
{
    public static readonly FakeAccount AuthorOne = new(70000001, "fake-author-one");
    public static readonly FakeAccount AuthorTwo = new(70000002, "fake-author-two");
    public static readonly FakeAccount AuthorThree = new(70000003, "fake-author-three");
    public static readonly FakeAccount AuthorFour = new(70000004, "fake-author-four");
    public static readonly FakeAccount AuthorFive = new(70000005, "fake-author-five");
    public static readonly FakeAccount Reviewer = new(70000011, "fake-reviewer-one");
    public static readonly FakeAccount OpsMember = new(70000021, "fake-ops-one");
    public static readonly FakeAccount Bot = new(70000031, "fake-checks[bot]", "Bot");

    /// <summary>Five authors: as many as the default team.</summary>
    public static IReadOnlyList<FakeAccount> Authors { get; } = [AuthorOne, AuthorTwo, AuthorThree, AuthorFour, AuthorFive];

    public static IReadOnlyList<FakeAccount> All { get; } = [.. Authors, Reviewer, OpsMember, Bot];
}

/// <summary>One review of a synthetic pull request. <see cref="SubmittedAt"/> is null for a pending review.</summary>
public sealed record FakeReview(long Id, FakeAccount? By, string State, DateTimeOffset? SubmittedAt, string Body);

/// <summary>A synthetic pull request, built fluently. Its title, branch and review bodies are text that may never be stored.</summary>
public sealed class FakePullRequest(FakeGitHub github, int number, FakeAccount? author, DateTimeOffset opened)
{
    public int Number => number;

    public FakeAccount? Author => author;

    public DateTimeOffset Opened => opened;

    public DateTimeOffset? MergedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public string Title => $"Synthetic title of pull request {number} that must never be stored";

    public string Branch => $"fake-branch-{number}-that-must-never-be-stored";

    public List<FakeReview> Reviews { get; } = [];

    /// <summary>What the list gives as <c>updated_at</c>: the latest of the opening, the submitted reviews, the merge and the close, as GitHub moves it.</summary>
    public DateTimeOffset Updated =>
        Reviews.Select(review => review.SubmittedAt).Append(MergedAt).Append(ClosedAt).Append(opened).OfType<DateTimeOffset>().Max();

    public FakePullRequest Merged(DateTimeOffset at)
    {
        (MergedAt, ClosedAt) = (at, at);
        return this;
    }

    /// <summary>Closed without a merge.</summary>
    public FakePullRequest Closed(DateTimeOffset at)
    {
        ClosedAt = at;
        return this;
    }

    /// <summary>A submitted review: <paramref name="state"/> is GitHub's (<c>APPROVED</c>, <c>COMMENTED</c>, <c>CHANGES_REQUESTED</c>, <c>DISMISSED</c>).</summary>
    public FakePullRequest Reviewed(FakeAccount? by, string state, DateTimeOffset at)
    {
        var id = github.NextReviewId();
        Reviews.Add(new FakeReview(id, by, state, at, $"Synthetic body of review {id} that must never be stored"));
        return this;
    }

    /// <summary>A review that was started and not submitted: it has no submission time.</summary>
    public FakePullRequest ReviewPending(FakeAccount by)
    {
        var id = github.NextReviewId();
        Reviews.Add(new FakeReview(id, by, "PENDING", null, $"Synthetic body of review {id} that must never be stored"));
        return this;
    }
}

/// <summary>
/// A synthetic GitHub behind the harness's HTTP stub: one repository's pull requests and their reviews, and
/// the Ops team's members, served as GitHub's REST API serves them, page by page with a <c>Link</c> header.
/// It answers under an Enterprise Server address, with the path prefix such a host has. Nothing here names a
/// real repository or person. Make one with <see cref="For"/>, which also writes the <c>github</c> setting.
/// </summary>
public sealed class FakeGitHub
{
    /// <summary>The API address in the configuration: an Enterprise Server's shape, on a name that cannot resolve.</summary>
    public const string ApiUrl = "https://github.example.invalid/api/v3/";

    public const string Repository = "fake-org/fake-sample";
    public const string OpsTeam = "fake-org/fake-ops";

    private const string Prefix = "/api/v3";
    private const string PullsPath = Prefix + "/repos/" + Repository + "/pulls";
    private const string MembersPath = Prefix + "/orgs/fake-org/teams/fake-ops/members";

    private readonly StubHttpHandler _handler;
    private long _reviewId = 880000;

    private FakeGitHub(StubHttpHandler handler)
    {
        _handler = handler;
        handler.On(HttpMethod.Get, PullsPath, request => Page(request, Sorted(request).Select(PullRequestJson)));
        handler.On(HttpMethod.Get, MembersPath, request => Page(request, OpsMembers.Select(AccountJson)));
    }

    /// <summary>A synthetic GitHub on the harness's stub, and the <c>github</c> setting that points the run at it.</summary>
    public static FakeGitHub For(PipelineHarness pipeline)
    {
        pipeline.Config["github"] = new JsonObject
        {
            ["apiUrl"] = ApiUrl,
            ["repositories"] = new JsonArray(Repository),
            ["opsTeam"] = OpsTeam,
        };
        return new FakeGitHub(pipeline.Jira.Handler);
    }

    public List<FakePullRequest> PullRequests { get; } = [];

    /// <summary>The Ops team's members as the next sync reads them.</summary>
    public List<FakeAccount> OpsMembers { get; } = [Accounts.OpsMember];

    /// <summary>The most rows any list gives per page, whatever the request asks for.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Every request that reached this host, as path and query.</summary>
    public IReadOnlyList<StubRequest> Requests => [.. _handler.Requests.Where(request => request.Path.StartsWith(Prefix + "/", StringComparison.Ordinal))];

    /// <summary>The requests for the list of pull requests, by the page each asked for.</summary>
    public IReadOnlyList<string> PullRequestPagesAsked => [.. Requests.Where(request => request.Path == PullsPath).Select(request => request.Query.GetValueOrDefault("page", "1"))];

    /// <summary>The numbers of the pull requests whose reviews were asked for, in the order asked.</summary>
    public IReadOnlyList<int> ReviewsAskedFor =>
        [.. Requests.Where(request => request.Path.EndsWith("/reviews", StringComparison.Ordinal)).Select(request => int.Parse(request.Path.Split('/')[^2], CultureInfo.InvariantCulture))];

    /// <summary>Text this host serves that may never be written anywhere: logins, account IDs, titles, branch names and review bodies.</summary>
    public IReadOnlyList<string> ForbiddenText =>
    [
        .. Accounts.All.SelectMany(account => new[] { account.Login, account.Id.ToString(CultureInfo.InvariantCulture) }),
        .. PullRequests.SelectMany(pullRequest => new[] { pullRequest.Title, pullRequest.Branch }),
        .. PullRequests.SelectMany(pullRequest => pullRequest.Reviews.Select(review => review.Body)),
    ];

    public FakePullRequest AddPullRequest(int number, FakeAccount? author, DateTimeOffset opened)
    {
        var pullRequest = new FakePullRequest(this, number, author, opened);
        PullRequests.Add(pullRequest);
        _handler.On(HttpMethod.Get, $"{PullsPath}/{number}/reviews", request => Page(request, pullRequest.Reviews.Select(ReviewJson)));
        return pullRequest;
    }

    internal long NextReviewId() => ++_reviewId;

    /// <summary>The pull requests in the order the request asks for: by last update when it says <c>sort=updated</c>, by number otherwise, in its direction.</summary>
    private IEnumerable<FakePullRequest> Sorted(StubRequest request)
    {
        var ascending = request.Query.GetValueOrDefault("sort") == "updated"
            ? PullRequests.OrderBy(pullRequest => pullRequest.Updated).ThenBy(pullRequest => pullRequest.Number)
            : PullRequests.OrderBy(pullRequest => pullRequest.Number);
        return request.Query.GetValueOrDefault("direction") == "asc" ? ascending : ascending.Reverse();
    }

    /// <summary>One page of a list, with GitHub's <c>Link</c> header naming the next page while there is one.</summary>
    private HttpResponseMessage Page(StubRequest request, IEnumerable<JsonObject> rows)
    {
        var all = rows.ToList();
        var page = int.Parse(request.Query.GetValueOrDefault("page", "1"), CultureInfo.InvariantCulture);
        var size = Math.Min(PageSize, int.Parse(request.Query.GetValueOrDefault("per_page", "30"), CultureInfo.InvariantCulture));
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new JsonArray([.. all.Skip((page - 1) * size).Take(size)]).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (page * size < all.Count)
        {
            var query = string.Join("&", request.Query.Where(pair => pair.Key != "page").Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}").Append($"page={page + 1}"));
            response.Headers.TryAddWithoutValidation("Link", $"<https://github.example.invalid{request.Path}?{query}>; rel=\"next\", <https://github.example.invalid{request.Path}?{query}>; rel=\"last\"");
        }

        return response;
    }

    private static JsonObject AccountJson(FakeAccount account) => new()
    {
        ["id"] = account.Id,
        ["login"] = account.Login,
        ["type"] = account.Type,
        ["html_url"] = $"https://github.example.invalid/{account.Login}",
    };

    private static JsonObject PullRequestJson(FakePullRequest pullRequest) => new()
    {
        ["number"] = pullRequest.Number,
        ["user"] = pullRequest.Author is { } author ? AccountJson(author) : null,
        ["title"] = pullRequest.Title,
        ["body"] = $"Synthetic description of pull request {pullRequest.Number}",
        ["head"] = new JsonObject { ["ref"] = pullRequest.Branch },
        ["state"] = pullRequest.ClosedAt is null ? "open" : "closed",
        ["draft"] = false,
        ["created_at"] = Time(pullRequest.Opened),
        ["updated_at"] = Time(pullRequest.Updated),
        ["closed_at"] = Time(pullRequest.ClosedAt),
        ["merged_at"] = Time(pullRequest.MergedAt),
    };

    private static JsonObject ReviewJson(FakeReview review)
    {
        var json = new JsonObject
        {
            ["id"] = review.Id,
            ["user"] = review.By is { } by ? AccountJson(by) : null,
            ["state"] = review.State,
            ["body"] = review.Body,
        };

        // GitHub leaves the submission time out of a pending review.
        if (review.SubmittedAt is { } at)
        {
            json["submitted_at"] = Time(at);
        }

        return json;
    }

    /// <summary>A time as GitHub writes it: UTC, to the second.</summary>
    private static JsonNode? Time(DateTimeOffset? value) =>
        value is { } time ? JsonValue.Create(time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)) : null;
}
