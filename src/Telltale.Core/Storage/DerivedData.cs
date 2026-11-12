namespace Telltale.Core.Storage;

/// <summary>One status interval of one issue. A null <see cref="End"/> means the issue is still in the status.</summary>
public sealed record IssueStatusInterval(string IssueId, string StatusId, DateTimeOffset Start, DateTimeOffset? End);

/// <summary>
/// What derive concluded about one issue. <see cref="CycleEnd"/> is the delivery moment, and
/// <see cref="DeliverySprintId"/> the sprint whose window contains it (null when it fell between sprints).
/// </summary>
public sealed record IssueCycle(string IssueId, DateTimeOffset? CycleEnd, bool Delivered, string? DeliverySprintId)
{
    /// <summary>When the cycle started. Null with the two day counts when the issue has no cycle time.</summary>
    public DateTimeOffset? CycleStart { get; init; }

    public double? CalendarDays { get; init; }

    public double? BusinessDays { get; init; }

    /// <summary>The story point estimate the issue is grouped by. Null means unestimated.</summary>
    public double? PointBucket { get; init; }

    /// <summary>How many times the issue moved out of a done-role status, delivered or not.</summary>
    public int ReopenCount { get; init; }
}

/// <summary>
/// The sprint window of one sprint of the configured board that has started. A null <see cref="End"/> means the sprint
/// is not completed and its window runs to the as-of time. This is the one place that says which sprints count:
/// the metric views read it and never decide for themselves.
/// </summary>
public sealed record DerivedSprintWindow(string SprintId, DateTimeOffset Start, DateTimeOffset? End);

/// <summary>Everything derive computes. It replaces the previous derived data as a whole (ADR-0002).</summary>
public sealed record DerivedData(
    IReadOnlyList<DerivedSprintWindow> SprintWindows,
    IReadOnlyList<IssueStatusInterval> StatusIntervals,
    IReadOnlyList<IssueCycle> IssueCycles);
