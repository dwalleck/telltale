namespace Telltale.Core.Storage;

/// <summary>The values of a pull request row's host. A pull request is identified by host, repository and number.</summary>
public static class PullRequestHosts
{
    public const string GitHub = "github";

    /// <summary>Azure DevOps.</summary>
    public const string Ado = "ado";
}

/// <summary>
/// The fields kept from a pull request (ADR-0002): when it was opened, merged and closed, and its author only
/// as a salted hash (ADR-0003). No title, branch name, description or login: a title and a branch are free
/// text and can carry an issue key or a name.
/// </summary>
public sealed record RawPullRequest
{
    /// <summary>One of <see cref="PullRequestHosts"/>.</summary>
    public required string Host { get; init; }

    /// <summary>The repository as the host's collector names it; for GitHub <c>owner/name</c> in lower case.</summary>
    public required string Repository { get; init; }

    /// <summary>The host's number of the pull request within the repository.</summary>
    public required long Number { get; init; }

    /// <summary>When the pull request was first opened, also for one opened as a draft or reopened since.</summary>
    public required DateTimeOffset Opened { get; init; }

    /// <summary>When it was merged. Null while it is not, and for one closed without a merge.</summary>
    public required DateTimeOffset? Merged { get; init; }

    /// <summary>When it was closed, merged or not. Null while it is open.</summary>
    public required DateTimeOffset? Closed { get; init; }

    /// <summary>
    /// When the host says the pull request last changed; null when it does not say. It is kept to ask less of
    /// the host: a pull request whose list entry is as stored has no event the store does not hold.
    /// </summary>
    public required DateTimeOffset? Updated { get; init; }

    /// <summary>The author as a salted hash. Null when the host names no author (a deleted account).</summary>
    public required string? AuthorHash { get; init; }

    /// <summary>
    /// One of <see cref="PullRequestHistoryStates"/>: whether the events this row was written with are in the
    /// store. A collector sets it only for a row it writes without having read the pull request's events.
    /// </summary>
    public string History { get; init; } = PullRequestHistoryStates.Read;
}

/// <summary>The values of <see cref="RawPullRequest.History"/>.</summary>
public static class PullRequestHistoryStates
{
    /// <summary>The store holds the events the host had when the row was written, however few.</summary>
    public const string Read = "read";

    /// <summary>
    /// The row was written by a collection that could not read the pull request's events (for GitHub, one that
    /// could not read the Ops group's members, so no review could be classified). The events are asked for at
    /// the next sync whatever the list entry says, and until they are read no wait that rests on them is stated.
    /// </summary>
    public const string NotRead = "not read";
}

/// <summary>
/// One event on a pull request that can end a wait, as it was classified when it was first fetched: what it
/// was and what kind of actor it came from. Who the actor is never reaches the store, hashed or not, so the
/// classification is made by the collector and is not made again: <see cref="Actor"/> says what the actor was
/// at the sync that first stored the event (for the Ops group, by the membership read at that sync).
/// </summary>
public sealed record RawPullRequestEvent
{
    public required string Host { get; init; }

    public required string Repository { get; init; }

    public required long Number { get; init; }

    /// <summary>One of <see cref="PullRequestEventKinds"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>The host's ID of the event within its kind (a review's ID): what makes a repeat of the event the same row.</summary>
    public required string EventId { get; init; }

    /// <summary>One of <see cref="PullRequestEventOutcomes"/>.</summary>
    public required string Outcome { get; init; }

    /// <summary>One of <see cref="PullRequestEventActors"/>.</summary>
    public required string Actor { get; init; }

    public required DateTimeOffset At { get; init; }
}

/// <summary>The values of <see cref="RawPullRequestEvent.Kind"/>. Another host's events (a vote, a comment on a file) are further values.</summary>
public static class PullRequestEventKinds
{
    /// <summary>A submitted review.</summary>
    public const string Review = "review";

    /// <summary>A reviewer's vote on Azure DevOps, other than a reset to no vote: its time is the vote's own thread's, never the reviewer's current vote, which has none.</summary>
    public const string Vote = "vote";

    /// <summary>A text comment on a file on Azure DevOps. A comment on the pull request as a whole is general conversation and is not stored.</summary>
    public const string FileComment = "file comment";
}

/// <summary>The values of <see cref="RawPullRequestEvent.Outcome"/>: what the event said, as the host reported it when the event was first fetched.</summary>
public static class PullRequestEventOutcomes
{
    public const string Approved = "approved";
    public const string ChangesRequested = "changes requested";
    public const string Commented = "commented";

    /// <summary>A review that had been dismissed by the time it was first fetched: a review, and not by itself an approval.</summary>
    public const string Dismissed = "dismissed";

    /// <summary>An outcome this version of the tool does not know.</summary>
    public const string Unknown = "unknown";
}

/// <summary>The values of <see cref="RawPullRequestEvent.Actor"/>. Only <see cref="Human"/> ends a wait for code review.</summary>
public static class PullRequestEventActors
{
    /// <summary>A person who is neither the author nor in the Ops group.</summary>
    public const string Human = "human";

    /// <summary>The pull request's own author.</summary>
    public const string Author = "author";

    /// <summary>A member of the configured Ops group at the sync that first stored the event.</summary>
    public const string Ops = "ops";

    /// <summary>An account the host marks as automation.</summary>
    public const string Automation = "automation";

    /// <summary>The host named no actor, or one of a type this version does not know: the event cannot be classified.</summary>
    public const string Unknown = "unknown";
}

/// <summary>
/// What the last collection from a pull request host came to. A host with no row was never read. Whether a
/// host is configured is not here: the configuration says that.
/// </summary>
/// <param name="Host">One of <see cref="PullRequestHosts"/>.</param>
/// <param name="State">One of <see cref="PullRequestHostStates"/>: how the last read of the host's pull requests ended.</param>
/// <param name="Roster">One of <see cref="PullRequestRosterStates"/>: how the last read of the Ops group's members ended.</param>
public sealed record RawPullRequestHost(string Host, string State, string Roster);

/// <summary>The values of <see cref="RawPullRequestHost.State"/>.</summary>
public static class PullRequestHostStates
{
    /// <summary>The last read ended and its rows were stored, however few: an empty answer is a read.</summary>
    public const string Read = "read";

    /// <summary>The last read failed. The rows of earlier reads stay as they were.</summary>
    public const string Failed = "failed";
}

/// <summary>The values of <see cref="RawPullRequestHost.Roster"/>.</summary>
public static class PullRequestRosterStates
{
    public const string Read = "read";
    public const string Failed = "failed";

    /// <summary>The read of the host failed and does not say how far the roster's got.</summary>
    public const string NotRead = "not read";

    /// <summary>No Ops group is configured for the host, so there was nothing to read.</summary>
    public const string NotConfigured = "not configured";
}

/// <summary>
/// One write of pull request rows: what a host's collector fetched, and what the read came to, in one
/// transaction. Rows are added to the stored ones and replace those of the same pull request; an event already
/// stored is left as it is, so its classification stays the first one. Nothing is ever deleted by a batch.
/// </summary>
/// <param name="SaltFingerprint">The fingerprint of the salt the authors were hashed with; a batch under another salt than the store's people were hashed with is refused.</param>
public sealed record PullRequestBatch(
    IReadOnlyList<RawPullRequest> PullRequests,
    IReadOnlyList<RawPullRequestEvent> Events,
    RawPullRequestHost HostState,
    string SaltFingerprint);
