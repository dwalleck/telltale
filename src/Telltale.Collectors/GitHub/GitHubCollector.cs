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

    public async Task<PullRequestBatch> FetchAsync(RawData stored, bool everything, CancellationToken cancellationToken)
    {
        using var github = new HostClient(_handler, "GitHub", _settings.ApiUrl, new AuthenticationHeaderValue("Bearer", _token), _clock, _notices);
        github.AddHeader("User-Agent", "telltale");
        github.AddHeader("X-GitHub-Api-Version", "2022-11-28");

        // The roster is read before any review is, so no review is classified without it. A roster that cannot
        // be read is never taken for an empty one: no review is read at all then, since a review is classified
        // once and keeps its class. The pull requests themselves are still read, because when one was opened
        // and merged does not depend on who reviewed it; each is marked as written without its reviews, which
        // has them asked for at the next sync and keeps the waits that rest on reviews from being stated.
        var opsMemberIds = new HashSet<long>();
        var rosterRead = true;
        if (_settings.OpsTeam is { } opsTeam)
        {
            try
            {
                await ForEachAsync(github, $"orgs/{opsTeam.Organization}/teams/{opsTeam.Slug}/members?per_page={PageSize}", "team members", member => opsMemberIds.Add(member.GetProperty("id").GetInt64()) || true, cancellationToken);
            }
            catch (UserFacingException exception) when (exception is not HostGaveUpException)
            {
                // Only a failure that is about the team: GitHub refused it (403, 404), or its list could not
                // be understood or read to an end. A client that gave up (no connection, the proxy, a rate
                // limit, a token GitHub does not take) says nothing of the team, and ends the read like any
                // other failed read, to be run again (owner decision D111).
                rosterRead = false;

                // The failure's own words may end by saying nothing was stored, which is true of a read that
                // stops there and not of this one: the pull requests are stored, without their reviews.
                await _notices.WriteLineAsync(
                    $"GitHub: the members of the Ops team could not be read ({exception.Message.Replace(" Nothing was stored.", "", StringComparison.Ordinal)}). "
                    + "No review was read, and no first code review or first code approval figure is given for GitHub until a sync reads the team.");
            }
        }

        var known = stored.PullRequests.Where(pullRequest => pullRequest.Host == Host).ToDictionary(pullRequest => (pullRequest.Repository, pullRequest.Number));
        var pullRequests = new Dictionary<(string Repository, long Number), (RawPullRequest Row, Func<long, bool> IsAuthor, bool ReadReviews)>();

        // Stopping at a page with nothing new is right only when the last read of the host ended: one that
        // failed may have stored some pull requests and not others (see below), in no order the list has, so
        // a page of stored ones says nothing about the pages after it. The read after a failed one, and the
        // first one, go through the whole list; that costs list pages only, never reviews already read.
        var readInFull = stored.PullRequestHosts.Any(host => host.Host == Host && host.State == PullRequestHostStates.Read);
        var wholeList = everything || !readInFull;

        // The start date the store's pull requests are collected under after this read, which the store
        // records: derive marks a sprint that began before it as covered in part. A read of the whole list
        // brings in everything from the configured date on, so the date is then the configured one. A sync
        // that stops where nothing is new does not go back to older pull requests, so a date moved back, or
        // taken out, takes effect only at a backfill: until then the later of the two dates stands.
        var storedFrom = PullRequestCollection.CollectedFrom(stored, Host);
        var collectedFrom = wholeList || storedFrom is null || _settings.CollectFrom > storedFrom ? _settings.CollectFrom : storedFrom;
        foreach (var repository in _settings.Repositories)
        {
            // Every state: an old pull request can be reviewed, merged or reopened at any time. The most
            // recently updated come first, so a sync can stop at the first page on which nothing is new: every
            // pull request after it was updated earlier still, and the store has it as it is. A backfill reads
            // on to the end, which is what brings in older pull requests after the start date is moved back.
            // A pull request repeated on a later page, as happens when one changes while the list is read, is one.
            await ForEachAsync(
                github,
                $"repos/{repository.Owner}/{repository.Name}/pulls?state=all&sort=updated&direction=desc&per_page={PageSize}",
                "pull requests",
                pullRequest =>
                {
                    var (row, authorId) = Read(repository, pullRequest);

                    // Out of what is collected: last updated before the start date. One that does not say
                    // when it was last updated cannot be shown to be that old, and is collected.
                    if (row.Updated < _settings.CollectFrom)
                    {
                        return null;
                    }

                    // The reviews are asked for only when the list entry says something may have happened
                    // that the store does not hold: the pull request is new to the store, or its merge, close
                    // or last update differs. An open one that names no last update is asked for every time.
                    // The store holds a pull request only with the reviews read for the entry it holds, since
                    // a batch is written whole or not at all, so an entry that is as stored needs no request.
                    // The exception is a row written while the roster could not be read: it says so itself.
                    known.TryGetValue((row.Repository, row.Number), out var before);
                    var readReviews = before is null || before.Merged != row.Merged || before.Closed != row.Closed || before.Updated != row.Updated
                        || (row.Updated is null && row.Closed is null)
                        || before.History != PullRequestHistoryStates.Read;
                    if (!readReviews && before == row)
                    {
                        return false;
                    }

                    pullRequests[(row.Repository, row.Number)] = (
                        readReviews && !rosterRead ? row with { History = PullRequestHistoryStates.NotRead } : row,
                        id => id == authorId,
                        readReviews && rosterRead);
                    return true;
                },
                cancellationToken,
                stopAtAPageWithNothingNew: !wholeList);
        }

        // A stored pull request whose reviews were not read is read now, whether or not the list pass came to
        // it: it can lie beyond the page a sync stops at, or before the start date, and while it stays unread no
        // wait that rests on reviews is stated for the whole host. Its list entry is not needed for that. Its
        // author is known here only as the stored hash, which is what a reviewer is compared with.
        // One of a repository that is no longer configured is left as it is: nothing is asked of that repository.
        if (rosterRead)
        {
            var configured = _settings.Repositories.Select(repository => repository.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var withoutReviews in known.Values.Where(pullRequest => pullRequest.History != PullRequestHistoryStates.Read
                && configured.Contains(pullRequest.Repository)
                && !pullRequests.ContainsKey((pullRequest.Repository, pullRequest.Number))))
            {
                pullRequests[(withoutReviews.Repository, withoutReviews.Number)] = (withoutReviews with { History = PullRequestHistoryStates.Read }, id => AuthorHash(id) == withoutReviews.AuthorHash, true);
            }
        }

        // The reviews are read one pull request at a time, and a pull request counts as read only with all of
        // them. When a request fails after some pull requests were read whole, those are given to be stored
        // with the failure: a first read of a large repository can need more requests than GitHub allows in an
        // hour, and would start from nothing at every attempt if a failed read kept nothing. Those not reached
        // are left out, row and all, so the next read finds them new or changed and asks for them. The host is
        // recorded as failed then, never as read, and the read after a failed one goes through the whole list.
        var events = new List<RawPullRequestEvent>();
        var unread = pullRequests.Where(pullRequest => pullRequest.Value.ReadReviews).Select(pullRequest => pullRequest.Key).ToHashSet();
        var toRead = unread.Count;
        try
        {
            foreach (var repository in _settings.Repositories)
            {
                foreach (var (row, isAuthor, _) in pullRequests.Values.Where(pullRequest => pullRequest.ReadReviews && pullRequest.Row.Repository == repository.Key).OrderBy(pullRequest => pullRequest.Row.Number))
                {
                    var itsEvents = new List<RawPullRequestEvent>();
                    await ForEachAsync(
                        github,
                        $"repos/{repository.Owner}/{repository.Name}/pulls/{row.Number.ToString(CultureInfo.InvariantCulture)}/reviews?per_page={PageSize}",
                        "pull request reviews",
                        review =>
                        {
                            if (ReadReview(row, isAuthor, opsMemberIds, review) is { } submitted)
                            {
                                itsEvents.Add(submitted);
                            }

                            return true;
                        },
                        cancellationToken);
                    events.AddRange(itsEvents);
                    unread.Remove((row.Repository, row.Number));
                }
            }
        }
        catch (UserFacingException failure) when (unread.Count < toRead)
        {
            // The failure's own words may end by saying nothing was stored, which is not true of this read.
            throw new PullRequestReadStoppedException(
                $"{failure.Message.Replace(" Nothing was stored.", "", StringComparison.Ordinal)} "
                + $"The reviews of {(toRead - unread.Count).ToString(CultureInfo.InvariantCulture)} of {toRead.ToString(CultureInfo.InvariantCulture)} pull requests were read before that and are stored, and the next sync reads the rest. "
                + "Until a read of GitHub finishes, a report says that GitHub's figures rest on a read that failed.",
                new PullRequestBatch(
                    [.. pullRequests.Where(pullRequest => !unread.Contains(pullRequest.Key)).Select(pullRequest => pullRequest.Value.Row)],
                    events,
                    new RawPullRequestHost(Host, PullRequestHostStates.Failed, PullRequestRosterStates.NotRead) { CollectFrom = storedFrom },
                    IdentityHash.SaltFingerprint(_salt)),
                failure);
        }

        return new PullRequestBatch(
            [.. pullRequests.Values.Select(pullRequest => pullRequest.Row)],
            events,
            new RawPullRequestHost(
                Host,
                PullRequestHostStates.Read,
                _settings.OpsTeam is null ? PullRequestRosterStates.NotConfigured : rosterRead ? PullRequestRosterStates.Read : PullRequestRosterStates.Failed)
            { CollectFrom = collectedFrom },
            IdentityHash.SaltFingerprint(_salt));
    }

    /// <summary>The kept fields of one pull request of the list, and its author's account ID, which is used to classify its reviews and goes no further.</summary>
    private (RawPullRequest Row, long? AuthorId) Read(GitHubRepository repository, JsonElement pullRequest)
    {
        var authorId = JiraJson.Optional(pullRequest, "user")?.GetProperty("id").GetInt64();

        // Times are in the form the store keeps (StoredTime), since a row is compared with the stored one.
        // GitHub gives times to the second today, so this changes nothing unless a host gives more.
        var merged = JiraJson.Optional(pullRequest, "merged_at") is { } mergedAt ? StoredTime.AsStored(JiraTime.Parse(mergedAt)) : (DateTimeOffset?)null;
        var closed = JiraJson.Optional(pullRequest, "closed_at") is { } closedAt ? StoredTime.AsStored(JiraTime.Parse(closedAt)) : (DateTimeOffset?)null;
        return (new RawPullRequest
        {
            Host = Host,
            Repository = repository.Key,
            Number = pullRequest.GetProperty("number").GetInt64(),

            // created_at is the first opening: it does not move when a draft is marked ready or a pull request is reopened.
            Opened = StoredTime.AsStored(JiraTime.Parse(pullRequest.GetProperty("created_at"))),

            // merged_at and never closed_at: a pull request closed without a merge has no merge time.
            Merged = merged,
            Closed = closed ?? merged,
            Updated = JiraJson.Optional(pullRequest, "updated_at") is { } updatedAt ? StoredTime.AsStored(JiraTime.Parse(updatedAt)) : null,
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
    private static RawPullRequestEvent? ReadReview(RawPullRequest pullRequest, Func<long, bool> isAuthor, HashSet<long> opsMemberIds, JsonElement review)
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
                (_, { } id) when isAuthor(id) => PullRequestEventActors.Author,
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
    /// <paramref name="read"/> says of each row whether it brought anything new, or null for a row older than
    /// what is collected: a page of only such rows ends the reading, since the rows after it are older still. With
    /// <paramref name="stopAtAPageWithNothingNew"/> the reading ends after the first page that has rows and
    /// none of them new, which is only right for a list whose order puts everything new first.
    /// </summary>
    private static async Task ForEachAsync(HostClient github, string firstPage, string what, Func<JsonElement, bool?> read, CancellationToken cancellationToken, bool stopAtAPageWithNothingNew = false)
    {
        var asked = new HashSet<string>(StringComparer.Ordinal);
        for (var next = firstPage; next is not null;)
        {
            var (rows, fresh, tooOld) = (0, 0, 0);
            if (!asked.Add(next) || asked.Count > MaxPagesPerList)
            {
                throw new UserFacingException($"GitHub's list of {what} did not come to an end: it named a page it had already given, or more than {MaxPagesPerList} pages. Nothing was stored.");
            }

            using var page = await github.GetPageAsync(next, cancellationToken);
            try
            {
                foreach (var item in page.Body.RootElement.EnumerateArray())
                {
                    rows++;
                    var brought = read(item);
                    fresh += brought == true ? 1 : 0;
                    tooOld += brought is null ? 1 : 0;
                }
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
            {
                // Names only the kind of failure, never a value from the answer.
                throw new UserFacingException($"GitHub's {what} answered in a shape this tool does not understand ({exception.GetType().Name}). Nothing was stored.", exception);
            }

            next = rows > 0 && (tooOld == rows || (stopAtAPageWithNothingNew && fresh == 0)) ? null : page.Next;
        }
    }
}
