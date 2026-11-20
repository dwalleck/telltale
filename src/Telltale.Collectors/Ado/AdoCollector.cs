using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Telltale.Collectors.Http;
using Telltale.Collectors.Jira;
using Telltale.Collectors.PullRequests;
using Telltale.Core;
using Telltale.Core.Configuration;
using Telltale.Core.Identity;
using Telltale.Core.Storage;

namespace Telltale.Collectors.Ado;

/// <summary>
/// Reads the pull requests of the configured Azure DevOps repositories and their threads over the Git REST
/// API, read-only. Only selected fields are kept (ADR-0002): when a pull request was opened and closed and
/// whether it was completed, and for each vote and each text comment on a file its time, what it said (a vote
/// in favour or against, a comment) and what kind of actor it came from. The author's identity ID is hashed
/// before anything leaves this class (ADR-0003), and a reviewer's identity never leaves it at all: an event is
/// classified here, by the configured automation identities as they are at this sync. No comment text, file
/// path, title or branch name is kept.
/// </summary>
public sealed class AdoCollector : IPullRequestCollector
{
    // The one version asked for, of every request. Azure DevOps Services has it, and Azure DevOps Server from
    // release 2022.1 on; an older Server refuses it. The README says so beside ado.apiUrl: there is no setting.
    private const string ApiVersion = "api-version=7.1";

    // How many pull requests one request of the list asks for. The list is asked again from where the last
    // answer ended, by the number of rows that came, so a host that gives fewer per answer is still read whole.
    private const int PageSize = 100;

    // The most answers read of one list: far more than a repository's pull requests or a pull request's threads.
    private const int MaxPagesPerList = 10_000;

    // The list is paged by position, so a pull request opened while it is read moves every later one down a
    // row and the next answer starts with a row already seen. A few answers of nothing but such rows can
    // happen; more of them in a row is a list that is not moving on.
    private const int MaxRepeatedPagesInARow = 3;

    // How many pull requests one after another may fail to give their threads before the read of the host is
    // given up as failed: the third such failure in a row stops it, and nothing of that read is stored.
    private const int MaxUnreadHistoriesInARow = 3;

    private readonly HttpMessageHandler _handler;
    private readonly AdoSettings _settings;
    private readonly string _token;
    private readonly string _salt;
    private readonly TimeProvider _clock;
    private readonly TextWriter _notices;

    /// <param name="handler">Carries every call: in production the one that uses the system proxy and certificate store.</param>
    /// <param name="token">The personal access token, from the secret store.</param>
    /// <param name="salt">The salt authors are hashed with.</param>
    /// <param name="clock">Times the waits before a failed request is sent again (see <see cref="HostClient"/>).</param>
    /// <param name="notices">Where each such wait is announced: standard error.</param>
    public AdoCollector(HttpMessageHandler handler, AdoSettings settings, string token, string salt, TimeProvider clock, TextWriter notices)
    {
        _handler = handler;
        _settings = settings;
        _token = token;
        _salt = salt;
        _clock = clock;
        _notices = notices;
    }

    public string Host => PullRequestHosts.Ado;

    public PullRequestSource Source => _settings.Source;

    public async Task<PullRequestBatch> FetchAsync(RawData stored, bool everything, CancellationToken cancellationToken)
    {
        // A personal access token is sent as the password of basic authentication, with no user name.
        using var ado = new HostClient(
            _handler, "Azure DevOps", _settings.OrganizationUrl,
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($":{_token}"))), _clock, _notices);

        var known = stored.PullRequests.Where(pullRequest => pullRequest.Host == Host).ToDictionary(pullRequest => (pullRequest.Repository, pullRequest.Number));
        var rows = new List<RawPullRequest>();
        var events = new List<RawPullRequestEvent>();
        var unreadInARow = 0;
        var storedWithoutThreads = new List<string>();
        foreach (var repository in _settings.Repositories)
        {
            // Every state: an old pull request can be reviewed, completed or reactivated at any time. The list
            // does not say when a pull request last changed and cannot be asked for the most recently changed
            // first, so it is read whole at every sync, a backfill or not.
            var listed = new Dictionary<long, (RawPullRequest Row, string? AuthorId)>();
            await ForEachPullRequestAsync(ado, repository, pullRequest =>
            {
                var read = Read(repository, pullRequest);
                return listed.TryAdd(read.Row.Number, read);
            }, cancellationToken);

            foreach (var (row, authorId) in listed.Values.OrderBy(pullRequest => pullRequest.Row.Number))
            {
                // Out of what is collected: closed before the start date. An open one cannot be shown to have
                // been without activity since then, and is collected.
                if (row.Closed < _settings.CollectFrom)
                {
                    continue;
                }

                // The threads are asked for when something may have happened that the store does not hold: the
                // pull request is new to the store, its closing or completion differs, or it is open, since
                // nothing in the list says whether an open pull request changed. A closed one that is stored
                // as closed was read after it closed, since a batch is written whole or not at all: what comes
                // after the closing ends no wait, and it costs no request again. The exception is a row that says its
                // threads were not read: they are asked for until they are.
                known.TryGetValue((row.Repository, row.Number), out var before);
                var readThreads = before is null || before.Merged != row.Merged || before.Closed != row.Closed || row.Closed is null
                    || before.History != PullRequestHistoryStates.Read;
                if (!readThreads && before == row)
                {
                    continue;
                }

                if (!readThreads)
                {
                    rows.Add(row);
                    continue;
                }

                // One pull request whose threads cannot be read, or cannot be read to their end or understood,
                // does not stop the others from being read: its row says so, none of its threads is stored
                // from this read, and no wait that rests on them is stated until a sync reads them. A few such
                // pull requests one after another is the host failing, not a pull request: that stops the read.
                // One that the store already holds as not read for this reason is not counted among them: it
                // failed at an earlier sync too, so its failing again says nothing new about the host, and a
                // few such pull requests would otherwise fail every later read of the host, where those
                // between them are closed and no longer asked for.
                var failedBefore = before?.History == PullRequestHistoryStates.Failed;
                var itsEvents = new List<RawPullRequestEvent>();
                try
                {
                    await ForEachThreadAsync(ado, repository, row.Number, thread => itsEvents.AddRange(ReadThread(row, authorId, thread)), cancellationToken);
                }
                catch (UserFacingException exception) when (failedBefore || ++unreadInARow < MaxUnreadHistoriesInARow)
                {
                    rows.Add(row with { History = PullRequestHistoryStates.Failed });
                    // The failure's own words end by saying nothing was stored, which is true of a read that
                    // stops there and not of this one: the pull request is stored, without its threads. That
                    // is said only once the read has ended, since a later failure can still stop it, and then
                    // this pull request is not stored either.
                    storedWithoutThreads.Add(
                        $"Azure DevOps: the threads of a pull request could not be read ({exception.Message.Replace(" Nothing was stored.", "", StringComparison.Ordinal)}). "
                        + "It is stored without them and counted as a pull request whose history was not read; its threads are asked for again at the next sync.");
                    continue;
                }

                unreadInARow = 0;
                rows.Add(row);
                events.AddRange(itsEvents);
            }
        }

        foreach (var notice in storedWithoutThreads)
        {
            await _notices.WriteLineAsync(notice);
        }

        // The list is read whole at every read, so what the store holds afterwards is everything from the
        // configured start date on: that date is recorded with it, for derive to mark the sprints before it.
        return new PullRequestBatch(
            rows,
            events,
            new RawPullRequestHost(Host, PullRequestHostStates.Read, PullRequestRosterStates.NotConfigured) { CollectFrom = _settings.CollectFrom, Repositories = [.. _settings.Repositories.Select(repository => repository.Key)] },
            IdentityHash.SaltFingerprint(_salt),
            _clock.GetUtcNow());
    }

    /// <summary>The kept fields of one pull request of the list, and its author's identity ID, which is used to classify its events and goes no further.</summary>
    private (RawPullRequest Row, string? AuthorId) Read(AdoRepository repository, JsonElement pullRequest)
    {
        var authorId = IdentityOf(JiraJson.Optional(pullRequest, "createdBy"));
        var status = JiraJson.Text(pullRequest, "status");

        // The closing time is taken only from a pull request that says it is closed: completed or abandoned.
        // One of those without a closing time is an answer in a shape the tool does not know.
        // Times are cut to what the store keeps (StoredTime): Azure DevOps gives seven digits of a second, and a
        // row is compared with the stored one to tell whether anything changed.
        DateTimeOffset? closed = status == "active" ? null
            : (status is "completed" or "abandoned") ? StoredTime.AsStored(JiraTime.Parse(pullRequest.GetProperty("closedDate")))
            : JiraJson.Optional(pullRequest, "closedDate") is { } closedDate ? StoredTime.AsStored(JiraTime.Parse(closedDate))
            : null;
        return (new RawPullRequest
        {
            Host = Host,
            Repository = repository.Key,
            Number = pullRequest.GetProperty("pullRequestId").GetInt64(),

            // creationDate is the first opening: it does not move when a draft is published or a pull request is reactivated.
            Opened = StoredTime.AsStored(JiraTime.Parse(pullRequest.GetProperty("creationDate"))),

            // Only completion is a merge: an abandoned pull request has a closing time too, and no merge time.
            Merged = status == "completed" ? closed : null,
            Closed = closed,

            // Azure DevOps's list does not say when a pull request last changed.
            Updated = null,
            AuthorHash = authorId is null ? null : IdentityHash.Of($"{PullRequestHosts.Ado}:{authorId}", _salt),
        }, authorId);
    }

    /// <summary>
    /// The events of one thread, classified. A thread that records a vote gives one event, unless the vote is a
    /// reset to no vote; a thread on a file gives one for each of its text comments; any other thread (general
    /// conversation, the host's own notes on pushes and policies) gives none. Nothing of an actor is kept but
    /// the class, and nothing of a comment but its time.
    /// </summary>
    private IEnumerable<RawPullRequestEvent> ReadThread(RawPullRequest pullRequest, string? authorId, JsonElement thread)
    {
        if (IsDeleted(thread))
        {
            yield break;
        }

        var threadId = thread.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture);
        var properties = JiraJson.Optional(thread, "properties");
        if (properties is { } recorded && PropertyText(recorded, "CodeReviewThreadType") == "VoteUpdate")
        {
            // The vote's value is the thread's own. Zero is a reset to no vote, which is no review. A value
            // that cannot be read is kept as unknown: it could have been a vote.
            int? vote = int.TryParse(PropertyText(recorded, "CodeReviewVoteResult"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;
            if (vote == 0)
            {
                yield break;
            }

            // Who voted is the identity the thread names as the voter, never the author of the thread's
            // comment: that is whoever, or whatever, wrote the system note. A voter the thread does not name
            // is not guessed at.
            var voterId = PropertyText(recorded, "CodeReviewVotedByIdentity") is { } key && JiraJson.Optional(thread, "identities") is { } identities
                ? IdentityOf(JiraJson.Optional(identities, key))
                : null;
            yield return Event(
                pullRequest, PullRequestEventKinds.Vote, threadId,
                vote switch { > 0 => PullRequestEventOutcomes.Approved, < 0 => PullRequestEventOutcomes.ChangesRequested, _ => PullRequestEventOutcomes.Unknown },
                ActorOf(voterId, authorId),
                JiraTime.Parse(thread.GetProperty("publishedDate")));
            yield break;
        }

        // A thread on a file names the file; one on the pull request as a whole has no such context.
        if (JiraJson.Optional(thread, "threadContext") is not { } context || JiraJson.Optional(context, "filePath") is null)
        {
            yield break;
        }

        foreach (var comment in thread.GetProperty("comments").EnumerateArray())
        {
            // Text comments only: the host's own notes in a thread are of another type. A deleted comment no
            // longer says what it was, and is passed over.
            if (IsDeleted(comment) || JiraJson.Optional(comment, "commentType")?.GetString() != "text")
            {
                continue;
            }

            yield return Event(
                pullRequest, PullRequestEventKinds.FileComment,
                $"{threadId}:{comment.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture)}",
                PullRequestEventOutcomes.Commented,
                ActorOf(IdentityOf(JiraJson.Optional(comment, "author")), authorId),
                JiraTime.Parse(comment.GetProperty("publishedDate")));
        }
    }

    private static RawPullRequestEvent Event(RawPullRequest pullRequest, string kind, string eventId, string outcome, string actor, DateTimeOffset at) => new()
    {
        Host = pullRequest.Host,
        Repository = pullRequest.Repository,
        Number = pullRequest.Number,
        Kind = kind,
        EventId = eventId,
        Outcome = outcome,
        Actor = actor,
        At = at,
    };

    /// <summary>
    /// What kind of actor an identity is. Azure DevOps does not mark automation, so only the configured
    /// identities are automation, and any other identity that is not the author is taken for a person. There
    /// is no Ops group here.
    /// </summary>
    private string ActorOf(string? identityId, string? authorId) =>
        identityId is null ? PullRequestEventActors.Unknown
        : identityId == authorId ? PullRequestEventActors.Author
        : _settings.AutomationIdentities.Contains(identityId) ? PullRequestEventActors.Automation
        : PullRequestEventActors.Human;

    /// <summary>An identity's ID in the form IDs are compared in, or null when there is no identity or its ID is not one.</summary>
    private static string? IdentityOf(JsonElement? identity) =>
        identity is { ValueKind: JsonValueKind.Object } named && JiraJson.Optional(named, "id") is { ValueKind: JsonValueKind.String } id
            ? AdoSettings.IdentityId(id.GetString())
            : null;

    private static bool IsDeleted(JsonElement item) => JiraJson.Optional(item, "isDeleted") is { ValueKind: JsonValueKind.True };

    /// <summary>
    /// A thread property's value as text, or null when the thread has no such property. Azure DevOps wraps a
    /// value as <c>{"$type": ..., "$value": ...}</c>; a bare value is read too.
    /// </summary>
    private static string? PropertyText(JsonElement properties, string name)
    {
        if (JiraJson.Optional(properties, name) is not { } property)
        {
            return null;
        }

        var value = property.ValueKind == JsonValueKind.Object ? JiraJson.Optional(property, "$value") : property;
        return value switch
        {
            { ValueKind: JsonValueKind.String } text => text.GetString(),
            { ValueKind: JsonValueKind.Number } number => number.GetRawText(),
            _ => null,
        };
    }

    /// <summary>
    /// Reads a repository's list of pull requests to its end. The list is paged by position: each request asks
    /// for the rows after those already given, and the list is over when an answer has no row. An answer with
    /// fewer rows than asked for is not taken for the last one, because a host may give fewer than it is asked
    /// for. <paramref name="read"/> says of each row whether it was new to this reading.
    /// </summary>
    private static async Task ForEachPullRequestAsync(HostClient ado, AdoRepository repository, Func<JsonElement, bool> read, CancellationToken cancellationToken)
    {
        var (skip, pages, repeatedPages) = (0, 0, 0);
        while (true)
        {
            if (++pages > MaxPagesPerList || repeatedPages > MaxRepeatedPagesInARow)
            {
                throw new UserFacingException($"Azure DevOps's list of pull requests did not come to an end: it gave the same pull requests again and again, or more than {MaxPagesPerList} pages. Nothing was stored.");
            }

            using var page = await ado.GetAsync($"{repository.ApiPath}/pullrequests?searchCriteria.status=all&$top={PageSize}&$skip={skip.ToString(CultureInfo.InvariantCulture)}&{ApiVersion}", cancellationToken);
            var (rows, fresh) = (0, 0);
            Read("pull requests", () =>
            {
                foreach (var pullRequest in page.RootElement.GetProperty("value").EnumerateArray())
                {
                    rows++;
                    fresh += read(pullRequest) ? 1 : 0;
                }
            });

            if (rows == 0)
            {
                return;
            }

            skip += rows;
            repeatedPages = fresh == 0 ? repeatedPages + 1 : 0;
        }
    }

    /// <summary>
    /// Reads every thread of a pull request. Azure DevOps gives them in one answer; should an answer say there
    /// is more (a continuation token), the rest is asked for with it, so a history is never taken for whole
    /// when the host says it is not.
    /// </summary>
    private static async Task ForEachThreadAsync(HostClient ado, AdoRepository repository, long number, Action<JsonElement> read, CancellationToken cancellationToken)
    {
        var first = $"{repository.ApiPath}/pullRequests/{number.ToString(CultureInfo.InvariantCulture)}/threads?{ApiVersion}";
        var given = new HashSet<string>(StringComparer.Ordinal);
        for (string? next = first; next is not null;)
        {
            using var page = await ado.GetContinuedPageAsync(next, cancellationToken);
            Read("pull request threads", () =>
            {
                foreach (var thread in page.Body.RootElement.GetProperty("value").EnumerateArray())
                {
                    read(thread);
                }
            });

            if (page.Continuation is not { } token)
            {
                return;
            }

            if (!given.Add(token) || given.Count > MaxPagesPerList)
            {
                throw new UserFacingException($"Azure DevOps's list of pull request threads did not come to an end: it gave a continuation token it had already given, or more than {MaxPagesPerList} pages. Nothing was stored.");
            }

            next = $"{first}&continuationToken={Uri.EscapeDataString(token)}";
        }
    }

    /// <summary>Reads an answer; one in a shape the tool does not know stops the command, naming only the kind of failure, never a value from the answer.</summary>
    private static void Read(string what, Action parse)
    {
        try
        {
            parse();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new UserFacingException($"Azure DevOps's {what} answered in a shape this tool does not understand ({exception.GetType().Name}). Nothing was stored.", exception);
        }
    }
}
