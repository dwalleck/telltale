using Telltale.Core.Workflow;

namespace Telltale.Core.Storage;

/// <summary>One status interval of one issue. A null <see cref="End"/> means the issue is still in the status.</summary>
public sealed record IssueStatusInterval(string IssueId, string StatusId, DateTimeOffset Start, DateTimeOffset? End);

/// <summary>
/// What derive concluded about one issue. <see cref="CycleEnd"/> is its last move into a done status. It is null when
/// the issue is not done now, and also when it is in a done status but its changelog ends in a status that is not done
/// (owner decision D8) or holds no move into done at all (D9): such an issue is not delivered. <see cref="Delivered"/> says whether that was a delivery: an abandoned issue has a
/// <see cref="CycleEnd"/> but is not delivered. Read <see cref="Delivered"/>, never "has a cycle end", to find deliveries.
/// <see cref="DeliverySprintId"/> is the sprint whose window contains a delivery (null when it fell between
/// sprints, and for every issue that is not delivered).
/// <see cref="ReopenCount"/> is how many times the issue moved out of a done-role status, delivered or not.
/// </summary>
public sealed record IssueCycle(string IssueId, DateTimeOffset? CycleEnd, bool Delivered, string? DeliverySprintId, int ReopenCount)
{
    /// <summary>The issue's cycle time. Null when it has none: it is not delivered, or it never entered a work-role status.</summary>
    public Cycle? Cycle { get; init; }

    /// <summary>
    /// The story point estimate the issue is grouped by. Set for delivered issues only: null for one that is
    /// unestimated, and for every issue that is not delivered, whatever its estimate.
    /// </summary>
    public double? PointBucket { get; init; }
}

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

/// <summary>The ways an issue's changelog can fail to line up. Each has one fixed message.</summary>
public enum ChangelogAnomalyKind
{
    /// <summary>A status change does not start where the one before it ended, is dated before the issue was created, or the history ends in a status the issue is not in.</summary>
    StatusHistory,

    /// <summary>A Sprint field change does not start from the sprints the one before it left, or the history ends in sprints the field does not list.</summary>
    SprintHistory,

    /// <summary>The issue is in a done-role status, but its status history ends in one that is not done: nothing dates a delivery (owner decision D8).</summary>
    DoneButHistoryEndsOutsideDone,

    /// <summary>The issue is in a done-role status and its history holds no move into one: it was created there (owner decision D9).</summary>
    DoneWithoutMoveIntoDone,
}

/// <summary>
/// One changelog anomaly of one issue. It can only say which issue and which kind: <see cref="Message"/> is the
/// kind's fixed text, so no name, summary or changelog value can travel in it to the store or a log.
/// </summary>
public sealed record IssueAnomaly(string IssueId, ChangelogAnomalyKind Kind)
{
    public string Message => Kind switch
    {
        ChangelogAnomalyKind.StatusHistory => "its status history does not line up",
        ChangelogAnomalyKind.SprintHistory => "its Sprint field history does not line up",
        ChangelogAnomalyKind.DoneButHistoryEndsOutsideDone => "it is in a done status but its status history ends outside done, so it is not counted as delivered",
        ChangelogAnomalyKind.DoneWithoutMoveIntoDone => "it is in a done status but its history holds no move into done, so it is not counted as delivered",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Not a changelog anomaly kind."),
    };
}

/// <summary>
/// What a rebuild of the derived data was built from, besides its as-of time: a digest that identifies the raw
/// data it read, and the settings its business days were counted with. <see cref="Holidays"/> is the holiday
/// list as ISO dates in order, separated by commas. When any of the three no longer matches, the derived data is stale.
/// </summary>
public sealed record DerivedSource(string RawDigest, string TimeZone, string Holidays);

/// <summary>Everything derive computes. It replaces the previous derived data as a whole (ADR-0002).</summary>
public sealed record DerivedData(
    IReadOnlyList<DerivedSprintWindow> SprintWindows,
    IReadOnlyList<IssueStatusInterval> StatusIntervals,
    IReadOnlyList<IssueCycle> IssueCycles,
    IReadOnlyList<IssueMembershipInterval> MembershipIntervals,
    IReadOnlyList<SprintStatusTime> SprintStatusTimes,
    IReadOnlyList<BoardStatus> BoardStatuses)
{
    /// <summary>The as-of time the rebuild used to close open intervals and the running sprint's window.</summary>
    public required DateTimeOffset AsOf { get; init; }

    /// <summary>The changelog anomalies found, at most one per issue and kind.</summary>
    public required IReadOnlyList<IssueAnomaly> Anomalies { get; init; }
}
