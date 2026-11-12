namespace Telltale.Core.Storage;

/// <summary>One status interval of one issue. A null <see cref="End"/> means the issue is still in the status.</summary>
public sealed record IssueStatusInterval(string IssueId, string StatusId, DateTimeOffset Start, DateTimeOffset? End);

/// <summary>
/// What derive concluded about one issue. <see cref="CycleEnd"/> is the delivery moment, and
/// <see cref="DeliverySprintId"/> the sprint whose window contains it (null when it fell between sprints).
/// </summary>
public sealed record IssueCycle(string IssueId, DateTimeOffset? CycleEnd, bool Delivered, string? DeliverySprintId);

/// <summary>
/// The sprint window of one sprint of the configured board that has started. A null <see cref="End"/> means the sprint
/// is not completed and its window runs to the as-of time. This is the one place that says which sprints count:
/// the metric views read it and never decide for themselves.
/// </summary>
public sealed record DerivedSprintWindow(string SprintId, DateTimeOffset Start, DateTimeOffset? End);

/// <summary>One membership interval of one issue in one sprint. A null <see cref="End"/> means the Sprint field still lists the sprint.</summary>
public sealed record IssueMembershipInterval(string IssueId, string SprintId, DateTimeOffset Start, DateTimeOffset? End);

/// <summary>
/// One issue's time in one status during one sprint (GLOSSARY: Time in status), measured three ways:
/// calendar time, business time and business days.
/// </summary>
public sealed record SprintStatusTime(string IssueId, string SprintId, string StatusId, TimeSpan CalendarTime, TimeSpan BusinessTime, double BusinessDays);

/// <summary>
/// A status the configuration gives a role, with its place in board order (the order the configuration
/// lists the statuses in, from 1) and whether its role is done.
/// </summary>
public sealed record BoardStatus(string StatusId, int Position, bool IsDone);

/// <summary>Everything derive computes. It replaces the previous derived data as a whole (ADR-0002).</summary>
public sealed record DerivedData(
    IReadOnlyList<DerivedSprintWindow> SprintWindows,
    IReadOnlyList<IssueStatusInterval> StatusIntervals,
    IReadOnlyList<IssueCycle> IssueCycles,
    IReadOnlyList<IssueMembershipInterval> MembershipIntervals,
    IReadOnlyList<SprintStatusTime> SprintStatusTimes,
    IReadOnlyList<BoardStatus> BoardStatuses);
