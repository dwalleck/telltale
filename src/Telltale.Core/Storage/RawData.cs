namespace Telltale.Core.Storage;

/// <summary>
/// The fields kept from a Jira issue (ADR-0002): no summary, description or comment, the
/// assignee only as a salted hash (ADR-0003), and the Jira issue ID but not the issue key, which
/// anyone can look up an assignee by (owner decision D52). <see cref="SprintIds"/> is what the Sprint field lists now:
/// sprint IDs with a comma between them (<c>41,42</c>), the form a Sprint change has, and empty when the issue is in no sprint. It is
/// null only for an issue stored before the field was fetched, whose sprints are therefore unknown.
/// </summary>
/// <remarks>Every member is named where an issue is built, so a field added later cannot slip into another's place.</remarks>
public sealed record RawIssue
{
    /// <summary>Jira's ID of the issue: digits.</summary>
    public required string IssueId { get; init; }

    public required string IssueTypeId { get; init; }

    public required DateTimeOffset Created { get; init; }

    /// <summary>The status the issue is in now.</summary>
    public required string StatusId { get; init; }

    public required string? ResolutionId { get; init; }

    /// <summary>The current estimate. Null when the issue has none.</summary>
    public required double? StoryPoints { get; init; }

    /// <summary>The assignee as a salted hash (ADR-0003). Null when the issue is unassigned.</summary>
    public required string? AssigneeHash { get; init; }

    public required DateTimeOffset Updated { get; init; }

    /// <summary>What the Sprint field lists now; see the summary.</summary>
    public required string? SprintIds { get; init; }
}

/// <summary>
/// One field change from an issue's changelog. <see cref="Field"/> is one of <see cref="ChangeFields"/>.
/// Status values are status IDs; sprint values are sprint IDs with a comma between them (<c>41,42</c>), as in
/// <see cref="RawIssue.SprintIds"/>; point values are numbers in invariant round-trip form, and null for no estimate.
/// </summary>
public sealed record RawChange(string HistoryId, string IssueId, DateTimeOffset At, string Field, string? From, string? To);

/// <summary>The values of <see cref="RawChange.Field"/>.</summary>
public static class ChangeFields
{
    public const string Status = "status";
    public const string Sprint = "sprint";
    public const string Points = "points";
}

/// <summary>
/// A sprint of the board. <see cref="State"/> is Jira's: one of <see cref="SprintStates"/>. Whether a sprint
/// has started is its state, not its dates: a future sprint may carry planned dates.
/// </summary>
public sealed record RawSprint(string SprintId, long BoardId, string Name, string State, DateTimeOffset? Start, DateTimeOffset? PlannedEnd, DateTimeOffset? Completed);

/// <summary>The values of <see cref="RawSprint.State"/>.</summary>
public static class SprintStates
{
    public const string Future = "future";
    public const string Active = "active";
    public const string Closed = "closed";
}

/// <summary>A workflow status: its ID, its current name and Jira's category key for it.</summary>
public sealed record RawStatus(string StatusId, string Name, string Category);

/// <summary>Raw rows: what a collector fetched, or everything the store holds.</summary>
public sealed record RawData(
    IReadOnlyList<RawIssue> Issues,
    IReadOnlyList<RawChange> Changes,
    IReadOnlyList<RawSprint> Sprints,
    IReadOnlyList<RawStatus> Statuses);

/// <summary>
/// One write to the raw tables: the rows a collector fetched and everything the store must do with them in
/// the same transaction. The store reads back <see cref="RawData"/>, never this.
/// </summary>
/// <param name="Rows">The rows fetched. They are added to the stored rows, replacing those with the same Jira IDs.</param>
/// <param name="Reached">The collector's watermark once the rows are written: no batch is written without saying how far it reaches.</param>
/// <param name="SaltFingerprint">
/// The fingerprint (<c>IdentityHash.SaltFingerprint</c>) of the salt the rows' person hashes were made with.
/// The store records it, and refuses a batch whose salt is not the one its other rows were hashed with,
/// unless the batch replaces them all.
/// </param>
public sealed record RawBatch(RawData Rows, Watermark Reached, string SaltFingerprint)
{
    /// <summary>
    /// The IDs of issues the collector saw and found out of scope (epics, sub-tasks, types above epic level).
    /// Writing the batch deletes whatever the store holds of them, since an issue stored while it was in scope
    /// can be converted later (owner decision D13).
    /// </summary>
    public IReadOnlyList<string> OutOfScopeIssueIds { get; init; } = [];

    /// <summary>
    /// Destructive: the rows are all the source holds (a backfill; owner decision D4), so every issue, change
    /// and sprint the store holds is removed and the rows are written in their place. Statuses are reference
    /// data (an ID's name) and are added and updated, never removed: stored history can name a status the
    /// source no longer lists.
    /// </summary>
    public bool ReplacesEverything { get; init; }
}

/// <summary>How many issues, sprints and changes the raw tables hold.</summary>
public sealed record RawCounts(long Issues, long Sprints, long Changes);
