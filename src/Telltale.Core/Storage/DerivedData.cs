using Telltale.Core.Configuration;
using Telltale.Core.Intervals;
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

    /// <summary>
    /// The issue's cycle time. Null when it has none: it is not delivered, or it never entered a work-role status,
    /// or its cycle starts with a brief visit and the treatment for cycle time leaves such cycles out (GLOSSARY:
    /// Treatment; owner decision D85).
    /// </summary>
    public Cycle? Cycle { get; private init; }

    /// <summary>
    /// The story point estimate the issue is grouped by. Set for delivered issues only: null for one that is
    /// unestimated, and for every issue that is not delivered, whatever its estimate.
    /// </summary>
    public double? PointBucket { get; private init; }

    /// <summary>
    /// Whether the issue's cycle starts with a brief visit (GLOSSARY: Brief visit; owner decisions D87 and D90;
    /// <see cref="Cycles.StartsWithBriefVisit"/>). True for delivered issues only, and for none when no status
    /// is listed. It is a flag and nothing else: no duration, no time.
    /// </summary>
    public bool StartsWithBriefVisit { get; private init; }

    /// <summary>
    /// A delivered issue. <paramref name="deliverySprintId"/> is null when the delivery fell between sprints,
    /// <paramref name="cycle"/> when the issue never entered a work-role status or its cycle was left out, and
    /// <paramref name="pointBucket"/> when it is unestimated. <paramref name="startsWithBriefVisit"/> says
    /// whether its cycle starts with a brief visit, which is recorded also when the cycle was left out for it.
    /// </summary>
    public static IssueCycle OfDelivered(string issueId, DateTimeOffset deliveredAt, string? deliverySprintId, Cycle? cycle, double? pointBucket, int reopenCount, bool startsWithBriefVisit) =>
        new(issueId, reopenCount) { CycleEnd = deliveredAt, Delivered = true, DeliverySprintId = deliverySprintId, Cycle = cycle, PointBucket = pointBucket, StartsWithBriefVisit = startsWithBriefVisit };

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
/// calendar time, business time and business days. The three are the time as recorded: no brief visit is left
/// out of them, whatever the treatment (GLOSSARY: Treatment), because the team of a sprint and the count of
/// brief visits are taken from the recorded rows (owner decision D93).
/// </summary>
public sealed record SprintStatusTime(string IssueId, string SprintId, string StatusId, TimeSpan CalendarTime, TimeSpan BusinessTime, double BusinessDays)
{
    /// <summary>
    /// The business days the time in status figures are taken from: <see cref="BusinessDays"/>, less the
    /// brief visits the treatment for time in status leaves out (owner decision D84). The same as
    /// <see cref="BusinessDays"/> when nothing is left out, and zero when nothing remains: the issue is then
    /// not in the status's row. Required, with no default, so that code that builds a row without measuring it
    /// does not compile, rather than derive quietly as if the treatment were <c>count</c>.
    /// </summary>
    public required double MeasuredBusinessDays { get; init; }
}

/// <summary>
/// One issue has at least one brief visit to one status that counts toward one sprint (GLOSSARY: Brief visit;
/// <see cref="Intervals.BriefVisits.During"/>). It holds the three IDs and nothing else: no duration, no
/// time, no number of visits.
/// </summary>
public sealed record SprintBriefVisit(string IssueId, string SprintId, string StatusId);

/// <summary>
/// A status the configuration gives a role, with its place in board order (the order the configuration
/// lists the statuses in, from 1), whether its role is done and whether its role is work.
/// </summary>
public sealed record BoardStatus(string StatusId, int Position, bool IsDone, bool IsWork);

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
/// <param name="BriefVisitThresholds">The brief visit thresholds (GLOSSARY: Brief visit) as a JSON object of status ID to minutes, the IDs in order, e.g. <c>{"10001":5,"10005":0.5}</c>; <c>{}</c> when no status is checked. The metric view <c>brief_visits_per_sprint</c> reads the listed statuses and their thresholds from the rebuild's record.</param>
/// <param name="PullRequestRepositories">The repositories whose pull requests the rebuild counted, as a JSON object of host to repository names, both in order, e.g. <c>{"github":["owner/name"]}</c>; <see cref="NoPullRequestRepositories"/> when no pull request host is configured. Left out, it is that.</param>
/// <param name="BriefVisitTreatment">The treatment of brief visits (GLOSSARY: Treatment) as a JSON object of each part and its label, the parts in order, e.g. <c>{"cycleTime":"count","timeInStatus":"exclude"}</c>. Left out, it is <see cref="BriefVisitsCounted"/>: a record made without it is of a rebuild that counted brief visits and changed nothing.</param>
public sealed record DerivedSource(
    string RawDigest,
    string TimeZone,
    string Holidays,
    string BaselineSprintIds,
    string StatusRoles,
    string AbandonedResolutionIds,
    long MinimumTeam,
    long MinimumAssigneesBehindAFigure,
    string BriefVisitThresholds,
    string BriefVisitTreatment = DerivedSource.BriefVisitsCounted,
    string PullRequestRepositories = DerivedSource.NoPullRequestRepositories)
{
    /// <summary><see cref="PullRequestRepositories"/> when no pull request host is configured. The one place that spells it.</summary>
    public const string NoPullRequestRepositories = "{}";

    /// <summary>Whether the rebuild counted pull requests at all: a pull request host was configured. A report has its pull request section only then.</summary>
    public bool PullRequestsCounted => PullRequestRepositories != NoPullRequestRepositories;

    /// <summary>
    /// <see cref="PullRequestRepositories"/> for the configured repositories by host: the form the store keeps,
    /// hosts and repositories in order, e.g. <c>{"github":["owner/name"]}</c>.
    /// </summary>
    public static string PullRequestRepositoriesOf(IReadOnlyDictionary<string, IReadOnlyList<string>> repositoriesByHost)
    {
        var ordered = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (host, repositories) in repositoriesByHost)
        {
            ordered.Add(host, [.. repositories.Order(StringComparer.Ordinal)]);
        }

        return System.Text.Json.JsonSerializer.Serialize(ordered);
    }

    /// <summary><see cref="BriefVisitThresholds"/> when no status is checked. The one place that spells it.</summary>
    public const string NoBriefVisitThresholds = "{}";

    /// <summary>Whether at least one status is listed in <see cref="BriefVisitThresholds"/>, so that the rebuild looked for brief visits.</summary>
    public bool BriefVisitsChecked => BriefVisitThresholds != NoBriefVisitThresholds;

    /// <summary>
    /// <see cref="BriefVisitThresholds"/> for the thresholds derive applies, by status ID: the form the store
    /// keeps and the view reads, with each threshold in minutes.
    /// </summary>
    public static string BriefVisitThresholdsOf(IReadOnlyDictionary<string, TimeSpan> thresholds)
    {
        if (thresholds.Count == 0)
        {
            return NoBriefVisitThresholds;
        }

        var minutes = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var (statusId, threshold) in thresholds)
        {
            minutes.Add(statusId, threshold.TotalMinutes);
        }

        return System.Text.Json.JsonSerializer.Serialize(minutes);
    }

    /// <summary><see cref="BriefVisitTreatment"/> when every part is <c>count</c>, which is also what an absent setting means (owner decision D83).</summary>
    public const string BriefVisitsCounted =
        "{\"" + BriefVisitTreatmentSetting.CycleTimeName + "\":\"" + BriefVisitTreatments.Count + "\",\"" + BriefVisitTreatmentSetting.TimeInStatusName + "\":\"" + BriefVisitTreatments.Count + "\"}";

    /// <summary><see cref="BriefVisitTreatment"/> for the treatment derive applies: the form the store keeps.</summary>
    public static string BriefVisitTreatmentOf(BriefVisitTreatmentSetting treatment) =>
        System.Text.Json.JsonSerializer.Serialize(new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [BriefVisitTreatmentSetting.CycleTimeName] = BriefVisitTreatments.LabelOf(treatment.CycleTime),
            [BriefVisitTreatmentSetting.TimeInStatusName] = BriefVisitTreatments.LabelOf(treatment.TimeInStatus),
        });

    /// <summary>
    /// Whether the rebuild left brief visits out of time in status: read from <see cref="BriefVisitTreatment"/>,
    /// the record of the treatment derive applied, and never from the configuration.
    /// </summary>
    public bool BriefVisitsLeftOutOfTimeInStatus => LeftOutOf(BriefVisitTreatmentSetting.TimeInStatusName);

    /// <summary>
    /// Whether the rebuild left the cycles that start with a brief visit out of cycle time: read from
    /// <see cref="BriefVisitTreatment"/>, as <see cref="BriefVisitsLeftOutOfTimeInStatus"/> is.
    /// </summary>
    public bool BriefVisitsLeftOutOfCycleTime => LeftOutOf(BriefVisitTreatmentSetting.CycleTimeName);

    /// <summary>
    /// Whether <see cref="BriefVisitTreatment"/> records, for <paramref name="part"/>, a treatment that leaves brief
    /// visits out. A record without the part, or with a label this version does not know, is not read as
    /// <c>count</c>: the derived data is then not this version's to read, and deriving again repairs it.
    /// </summary>
    private bool LeftOutOf(string part)
    {
        using var recorded = System.Text.Json.JsonDocument.Parse(BriefVisitTreatment);
        if (recorded.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object
            || !recorded.RootElement.TryGetProperty(part, out var label)
            || label.ValueKind != System.Text.Json.JsonValueKind.String
            || !BriefVisitTreatments.TryParse(label.GetString(), out var treatment))
        {
            throw new UserFacingException("The derived data in the store records a treatment of brief visits this version of the tool does not know. Run derive first.");
        }

        return BriefVisitTreatments.LeavesOut(treatment);
    }

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

    /// <summary>The waits of the pull requests of the configured repositories: at most one per pull request and measure. Empty when no pull request host is configured.</summary>
    public IReadOnlyList<PullRequestWait> PullRequestWaits { get; init; } = [];

    /// <summary>The measures a pull request can have a wait for, in the order a report lists them.</summary>
    public IReadOnlyList<string> PullRequestMeasures { get; init; } = [];
}

/// <summary>
/// One wait of one pull request: from its opening to the event that ended the wait for <see cref="Measure"/>
/// (see <c>PullRequestTiming</c>). <see cref="SprintId"/> is the sprint whose window holds <see cref="Ended"/>;
/// null when no window or more than one holds it. <see cref="ElapsedDays"/> is the whole wait divided by 24
/// hours, and <see cref="BusinessDays"/> the same wait in business days; neither is clipped to a sprint.
/// <see cref="AuthorHash"/> is the pull request's author, for the minimum group size only.
/// </summary>
public sealed record PullRequestWait(
    string Host,
    string Repository,
    long Number,
    string Measure,
    string? AuthorHash,
    DateTimeOffset Opened,
    DateTimeOffset Ended,
    double ElapsedDays,
    double BusinessDays,
    string? SprintId);
