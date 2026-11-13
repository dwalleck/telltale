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
/// <remarks>
/// Built only by <see cref="OfDelivered"/>, <see cref="OfAbandoned"/> and <see cref="OfNotDelivered"/>, one for each
/// outcome, so the members cannot contradict each other: only a delivered issue has a delivery sprint, a cycle
/// time or a point bucket, and only an issue that reached done has a cycle end.
/// </remarks>
public sealed record IssueCycle
{
    private IssueCycle(string issueId, int reopenCount)
    {
        IssueId = issueId;
        ReopenCount = reopenCount;
    }

    public string IssueId { get; }

    public DateTimeOffset? CycleEnd { get; private init; }

    public bool Delivered { get; private init; }

    public string? DeliverySprintId { get; private init; }

    public int ReopenCount { get; }

    /// <summary>The issue's cycle time. Null when it has none: it is not delivered, or it never entered a work-role status.</summary>
    public Cycle? Cycle { get; private init; }

    /// <summary>
    /// The story point estimate the issue is grouped by. Set for delivered issues only: null for one that is
    /// unestimated, and for every issue that is not delivered, whatever its estimate.
    /// </summary>
    public double? PointBucket { get; private init; }

    /// <summary>
    /// A delivered issue. <paramref name="deliverySprintId"/> is null when the delivery fell between sprints,
    /// <paramref name="cycle"/> when the issue never entered a work-role status, and
    /// <paramref name="pointBucket"/> when it is unestimated.
    /// </summary>
    public static IssueCycle OfDelivered(string issueId, DateTimeOffset deliveredAt, string? deliverySprintId, Cycle? cycle, double? pointBucket, int reopenCount) =>
        new(issueId, reopenCount) { CycleEnd = deliveredAt, Delivered = true, DeliverySprintId = deliverySprintId, Cycle = cycle, PointBucket = pointBucket };

    /// <summary>An abandoned issue: it reached done at <paramref name="closedAt"/>, which is its cycle end, and that is not a delivery.</summary>
    public static IssueCycle OfAbandoned(string issueId, DateTimeOffset closedAt, int reopenCount) =>
        new(issueId, reopenCount) { CycleEnd = closedAt };

    /// <summary>An issue that is not done, or whose history gives no delivery moment.</summary>
    public static IssueCycle OfNotDelivered(string issueId, int reopenCount) => new(issueId, reopenCount);
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
/// One issue has at least one brief visit to one status that counts toward one sprint (GLOSSARY: Brief visit;
/// <see cref="Intervals.BriefVisits.ForSprint"/>). It holds the three IDs and nothing else: no duration, no
/// time, no number of visits.
/// </summary>
public sealed record SprintBriefVisit(string IssueId, string SprintId, string StatusId);

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
/// data it read, and every setting a derived value depends on. When any of them no longer matches, the derived
/// data is stale. The settings that decide what was fetched are not here: the store records them with the raw
/// data (<see cref="CollectionScope"/>).
/// </summary>
/// <param name="RawDigest">Identifies the raw data the rebuild read.</param>
/// <param name="TimeZone">The time zone business days were counted in.</param>
/// <param name="Holidays">The holiday list as ISO dates in order, separated by commas.</param>
/// <param name="BaselineSprintIds">The baseline sprint list the baseline views pool over, as a JSON array of the IDs in order, e.g. <c>["41","42"]</c>; <c>[]</c> when there is no baseline.</param>
/// <param name="StatusRoles">Each status and its role in board order, e.g. <c>10001=work,10002=done</c>.</param>
/// <param name="AbandonedResolutionIds">The abandoned resolution IDs in order, separated by commas.</param>
/// <param name="MinimumTeam">The minimum group size's team number (GLOSSARY: Minimum group size): the metric views read it from the rebuild's record and never from the configuration.</param>
/// <param name="MinimumAssigneesBehindAFigure">The minimum group size's number of assignees behind a figure, read by the views in the same way.</param>
public sealed record DerivedSource(
    string RawDigest,
    string TimeZone,
    string Holidays,
    string BaselineSprintIds,
    string StatusRoles,
    string AbandonedResolutionIds,
    long MinimumTeam,
    long MinimumAssigneesBehindAFigure)
{
    /// <summary>
    /// The brief visit thresholds (GLOSSARY: Brief visit) as a JSON object of status ID to minutes, the IDs in
    /// order, e.g. <c>{"10001":5,"10005":0.5}</c>; <c>{}</c> when no status is checked. The metric view
    /// <c>brief_visits_per_sprint</c> reads the listed statuses and their thresholds from the rebuild's record.
    /// </summary>
    public string BriefVisitThresholds { get; init; } = "{}";

    /// <summary><see cref="BriefVisitThresholds"/> for thresholds in minutes by status ID: the form the store keeps and the view reads.</summary>
    public static string BriefVisitThresholdsOf(IReadOnlyDictionary<string, double> minutesByStatusId) =>
        System.Text.Json.JsonSerializer.Serialize(new SortedDictionary<string, double>(minutesByStatusId.ToDictionary(), StringComparer.Ordinal));

    /// <summary><see cref="BaselineSprintIds"/> for a list of sprint IDs: the form the store keeps and the views read.</summary>
    public static string BaselineSprintIdsOf(IEnumerable<string> sprintIds) =>
        System.Text.Json.JsonSerializer.Serialize(sprintIds.Order(StringComparer.Ordinal));
}

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

    /// <summary>
    /// The IDs of the closed sprints that have no sprint window because Jira gives them neither a completion
    /// time nor a planned end (owner decision D51). They are in no derived table; the run names them.
    /// </summary>
    public IReadOnlyList<string> ClosedSprintsWithoutAnEnd { get; init; } = [];

    /// <summary>The brief visits counted toward a sprint: at most one per issue, sprint and status. Empty when no status is checked.</summary>
    public IReadOnlyList<SprintBriefVisit> BriefVisits { get; init; } = [];
}
