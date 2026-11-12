namespace Telltale.Core.Storage;

/// <summary>
/// The fields kept from a Jira issue (ADR-0002): no summary, description or comment, and the
/// assignee only as a salted hash (ADR-0003). <see cref="SprintIds"/> is what the Sprint field lists now:
/// comma-separated sprint IDs, the form a Sprint change has, and empty when the issue is in no sprint. It is
/// null only for an issue stored before the field was fetched, whose sprints are therefore unknown.
/// </summary>
public sealed record RawIssue(
    string IssueId,
    string Key,
    string IssueTypeId,
    DateTimeOffset Created,
    string StatusId,
    string? ResolutionId,
    double? StoryPoints,
    string? AssigneeHash,
    DateTimeOffset Updated,
    string? SprintIds);

/// <summary>
/// One field change from an issue's changelog. <see cref="Field"/> is one of <see cref="ChangeFields"/>.
/// Status values are status IDs; sprint values are comma-separated sprint IDs; point values are numbers in
/// invariant round-trip form, and null for no estimate.
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

/// <summary>Raw rows, either a batch on its way into the store or everything the store holds.</summary>
public sealed record RawData(
    IReadOnlyList<RawIssue> Issues,
    IReadOnlyList<RawChange> Changes,
    IReadOnlyList<RawSprint> Sprints,
    IReadOnlyList<RawStatus> Statuses)
{
    /// <summary>
    /// In a batch: the IDs of issues the collector saw and found out of scope (epics, sub-tasks, types above
    /// epic level). Writing the batch deletes whatever the store holds of them, since an issue stored while it
    /// was in scope can be converted later (owner decision D13). Always empty in what the store reads back.
    /// </summary>
    public IReadOnlyList<string> OutOfScopeIssueIds { get; init; } = [];

    /// <summary>
    /// In a batch: true when the batch is everything Jira holds for the configuration (a backfill). Writing it
    /// removes every raw row first, so what the store holds afterwards is this batch and nothing else: a row
    /// for something that has since left Jira does not stay (owner decision D4). Always false in what the
    /// store reads back.
    /// </summary>
    public bool ReplacesEverything { get; init; }
}
