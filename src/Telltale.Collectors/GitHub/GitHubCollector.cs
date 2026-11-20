using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Telltale.Collectors.Http;
using Telltale.Collectors.Jira;
using Telltale.Collectors.PullRequests;
using Telltale.Core;
using Telltale.Core.Configuration;
using Telltale.Core.Identity;
using Telltale.Core.Storage;

namespace Telltale.Collectors.GitHub;

/// <summary>
/// Reads the pull requests of the configured GitHub repositories and their submitted reviews over the REST
/// API, read-only. Only selected fields are kept (ADR-0002): when a pull request was opened, merged and
/// closed, and for each review when it was submitted, what it said and what kind of actor it came from. The
/// author's account ID is hashed before anything leaves this class (ADR-0003), and a reviewer's identity never
/// leaves it at all: a review is classified here, by the Ops group's members as they are at this sync.
/// </summary>
public sealed class GitHubCollector : IPullRequestCollector
{
    // The most GitHub gives per page.
    private const int PageSize = 100;

    // The most pages read from one list: a million rows, far more than a repository's pull requests. A list
    // that names a next page beyond it, or the same page twice, is not one the tool can read to its end.
    private const int MaxPagesPerList = 10_000;

    private readonly HttpMessageHandler _handler;
    private readonly GitHubSettings _settings;
    private readonly string _token;
    private readonly string _salt;
    private readonly TimeProvider _clock;
    private readonly TextWriter _notices;

    /// <param name="handler">Carries every call: in production the one that uses the system proxy and certificate store.</param>
    /// <param name="token">The GitHub token, from the secret store.</param>
    /// <param name="salt">The salt authors are hashed with.</param>
    /// <param name="clock">Times the waits before a failed request is sent again (see <see cref="HostClient"/>).</param>
    /// <param name="notices">Where each such wait is announced: standard error.</param>
    public GitHubCollector(HttpMessageHandler handler, GitHubSettings settings, string token, string salt, TimeProvider clock, TextWriter notices)
    {
        _handler = handler;
        _settings = settings;
        _token = token;
        _salt = salt;
        _clock = clock;
        _notices = notices;
    }

    public string Host => PullRequestHosts.GitHub;

    public async Task<PullRequestBatch> FetchAsync(RawData stored, CancellationToken cancellationToken)
    {
        using var github = new HostClient(_handler, "GitHub", _settings.ApiUrl, new AuthenticationHeaderValue("Bearer", _token), _clock, _notices);
        github.AddHeader("User-Agent", "telltale");
        github.AddHeader("X-GitHub-Api-Version", "2022-11-28");

        // The roster is read before any review is, so no review is classified without it: a roster that cannot
        // be read fails the whole read, and is never taken for an empty one.
        var opsMemberIds = new HashSet<long>();
        if (_settings.OpsTeam is { } opsTeam)
        {
            await ForEachAsync(github, $"orgs/{opsTeam.Organization}/teams/{opsTeam.Slug}/members?per_page={PageSize}", "team members", member => opsMemberIds.Add(member.GetProperty("id").GetInt64()), cancellationToken);
        }

        var known = stored.PullRequests.Where(pullRequest => pullRequest.Host == Host).ToDictionary(pullRequest => (pullRequest.Repository, pullRequest.Number));
        var pullRequests = new Dictionary<(string Repository, long Number), (RawPullRequest Row, long? AuthorId)>();
        foreach (var repository in _settings.Repositories)
        {
            // Every state, at every sync: an old pull request can be reviewed, merged or reopened at any time.
            // A pull request repeated on a later page, as happens when one is opened while the list is read, is one.
            await ForEachAsync(
                github,
                $"repos/{repository.Owner}/{repository.Name}/pulls?state=all&sort=created&direction=asc&per_page={PageSize}",
                "pull requests",
                pullRequest =>
                {
                    var (row, authorId) = Read(repository, pullRequest);
                    pullRequests[(row.Repository, row.Number)] = (row, authorId);
                },
                cancellationToken);
        }

        var events = new List<RawPullRequestEvent>();
        foreach (var repository in _settings.Repositories)
        {
            foreach (var (row, authorId) in pullRequests.Values.Where(pullRequest => pullRequest.Row.Repository == repository.Key).OrderBy(pullRequest => pullRequest.Row.Number))
            {
                // Only a review made before a pull request closed can end a wait, and those of a pull request
                // the store already holds as closed at this same moment were read when it was first seen
                // closed. So each sync asks for the reviews of the pull requests that are open, or were when
                // last read: one request for each, not one for every pull request the repository ever had.
                if (row.Closed is not null && known.TryGetValue((row.Repository, row.Number), out var before) && before.Closed == row.Closed)
                {
                    continue;
                }

                await ForEachAsync(
                    github,
                    $"repos/{repository.Owner}/{repository.Name}/pulls/{row.Number.ToString(CultureInfo.InvariantCulture)}/reviews?per_page={PageSize}",
                    "pull request reviews",
                    review =>
                    {
                        if (ReadReview(row, authorId, opsMemberIds, review) is { } submitted)
                        {
                            events.Add(submitted);
                        }
                    },
                    cancellationToken);
            }
        }

        return new PullRequestBatch(
            [.. pullRequests.Values.Select(pullRequest => pullRequest.Row)],
            events,
            new RawPullRequestHost(Host, PullRequestHostStates.Read, _settings.OpsTeam is null ? PullRequestRosterStates.NotConfigured : PullRequestRosterStates.Read),
            IdentityHash.SaltFingerprint(_salt));
    }

    /// <summary>The kept fields of one pull request of the list, and its author's account ID, which is used to classify its reviews and goes no further.</summary>
    private (RawPullRequest Row, long? AuthorId) Read(GitHubRepository repository, JsonElement pullRequest)
    {
        var authorId = JiraJson.Optional(pullRequest, "user")?.GetProperty("id").GetInt64();
        var merged = JiraJson.Optional(pullRequest, "merged_at") is { } mergedAt ? JiraTime.Parse(mergedAt) : (DateTimeOffset?)null;
        var closed = JiraJson.Optional(pullRequest, "closed_at") is { } closedAt ? JiraTime.Parse(closedAt) : (DateTimeOffset?)null;
        return (new RawPullRequest
        {
            Host = Host,
            Repository = repository.Key,
            Number = pullRequest.GetProperty("number").GetInt64(),

            // created_at is the first opening: it does not move when a draft is marked ready or a pull request is reopened.
            Opened = JiraTime.Parse(pullRequest.GetProperty("created_at")),

            // merged_at and never closed_at: a pull request closed without a merge has no merge time.
            Merged = merged,
            Closed = closed ?? merged,
            AuthorHash = authorId is { } id ? AuthorHash(id) : null,
        }, authorId);
    }

    /// <summary>The host's name is part of what is hashed, so an account of another host with the same number is not the same person.</summary>
    private string AuthorHash(long accountId) =>
        IdentityHash.Of($"{PullRequestHosts.GitHub}:{accountId.ToString(CultureInfo.InvariantCulture)}", _salt);

    /// <summary>
    /// One review as an event, classified, or null for one that was not submitted (a pending review has no
    /// submission time and is not a review yet). Nothing of the reviewer is kept but the class.
    /// </summary>
    private static RawPullRequestEvent? ReadReview(RawPullRequest pullRequest, long? authorId, HashSet<long> opsMemberIds, JsonElement review)
    {
        if (JiraJson.Optional(review, "submitted_at") is not { } submittedAt)
        {
            return null;
        }

        var reviewer = JiraJson.Optional(review, "user");
        var reviewerId = reviewer?.GetProperty("id").GetInt64();
        var type = reviewer is { } named ? JiraJson.Text(named, "type") : null;
        return new RawPullRequestEvent
        {
            Host = pullRequest.Host,
            Repository = pullRequest.Repository,
            Number = pullRequest.Number,
            Kind = PullRequestEventKinds.Review,
            EventId = review.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture),
            Outcome = JiraJson.Text(review, "state") switch
            {
                "APPROVED" => PullRequestEventOutcomes.Approved,
                "CHANGES_REQUESTED" => PullRequestEventOutcomes.ChangesRequested,
                "COMMENTED" => PullRequestEventOutcomes.Commented,
                "DISMISSED" => PullRequestEventOutcomes.Dismissed,
                _ => PullRequestEventOutcomes.Unknown,
            },

            // GitHub's account type tells its own automation apart ("Bot"); it cannot show that a "User" is a
            // person, and an account of any other type, or none, is not guessed at.
            Actor = (type, reviewerId) switch
            {
                ("Bot", _) => PullRequestEventActors.Automation,
                (_, null) => PullRequestEventActors.Unknown,
                (_, { } id) when id == authorId => PullRequestEventActors.Author,
                (_, { } id) when opsMemberIds.Contains(id) => PullRequestEventActors.Ops,
                ("User", _) => PullRequestEventActors.Human,
                _ => PullRequestEventActors.Unknown,
            },
            At = JiraTime.Parse(submittedAt),
        };
    }

    /// <summary>
    /// Reads a list to its end, following the next page each answer names and never asking past the last one
    /// named: a list is over when an answer names no next page, not when a page comes back empty.
    /// </summary>
    private static async Task ForEachAsync(HostClient github, string firstPage, string what, Action<JsonElement> read, CancellationToken cancellationToken)
    {
        var asked = new HashSet<string>(StringComparer.Ordinal);
        for (var next = firstPage; next is not null;)
        {
            if (!asked.Add(next) || asked.Count > MaxPagesPerList)
            {
                throw new UserFacingException($"GitHub's list of {what} did not come to an end: it named a page it had already given, or more than {MaxPagesPerList} pages. Nothing was stored.");
            }

            using var page = await github.GetPageAsync(next, cancellationToken);
            try
            {
                foreach (var item in page.Body.RootElement.EnumerateArray())
                {
                    read(item);
                }
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
            {
                // Names only the kind of failure, never a value from the answer.
                throw new UserFacingException($"GitHub's {what} answered in a shape this tool does not understand ({exception.GetType().Name}). Nothing was stored.", exception);
            }

            next = page.Next;
        }
    }
}
