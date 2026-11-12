namespace Telltale.Core.Storage;

/// <summary>One status interval of one issue. A null <see cref="End"/> means the issue is still in the status.</summary>
public sealed record IssueStatusInterval(string IssueId, string StatusId, DateTimeOffset Start, DateTimeOffset? End);

/// <summary>
/// What derive concluded about one issue. <see cref="CycleEnd"/> is the delivery moment, and
/// <see cref="DeliverySprintId"/> the sprint whose window contains it (null when it fell between sprints).
/// </summary>
public sealed record IssueCycle(string IssueId, DateTimeOffset? CycleEnd, bool Delivered, string? DeliverySprintId);

/// <summary>Everything derive computes. It replaces the previous derived data as a whole (ADR-0002).</summary>
public sealed record DerivedData(IReadOnlyList<IssueStatusInterval> StatusIntervals, IReadOnlyList<IssueCycle> IssueCycles);
