namespace Telltale.Core.Storage;

/// <summary>
/// The fields kept from a Jira issue (ADR-0002): no summary, description or comment, and the
/// assignee only as a salted hash (ADR-0003).
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
    DateTimeOffset Updated);

/// <summary>
/// One field change from an issue's changelog. <see cref="Field"/> is one of <see cref="ChangeFields"/>.
/// Status values are status IDs.
/// </summary>
public sealed record RawChange(string HistoryId, string IssueId, DateTimeOffset At, string Field, string? From, string? To);

/// <summary>The values of <see cref="RawChange.Field"/>.</summary>
public static class ChangeFields
{
    public const string Status = "status";
}

/// <summary>A sprint of the board. A future sprint has no start; an uncompleted one has no completion time.</summary>
public sealed record RawSprint(string SprintId, long BoardId, string Name, string State, DateTimeOffset? Start, DateTimeOffset? PlannedEnd, DateTimeOffset? Completed);

/// <summary>A workflow status: its ID, its current name and Jira's category key for it.</summary>
public sealed record RawStatus(string StatusId, string Name, string Category);

/// <summary>Raw rows, either a batch on its way into the store or everything the store holds.</summary>
public sealed record RawData(
    IReadOnlyList<RawIssue> Issues,
    IReadOnlyList<RawChange> Changes,
    IReadOnlyList<RawSprint> Sprints,
    IReadOnlyList<RawStatus> Statuses);
