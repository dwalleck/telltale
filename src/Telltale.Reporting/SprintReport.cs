using System.Data.Common;
using System.Globalization;
using System.Text;
using Telltale.Core;
using Telltale.Core.Configuration;
using Telltale.Core.Derive;
using Telltale.Core.Storage;

namespace Telltale.Reporting;

/// <summary>
/// The markdown report for one sprint. It reads metric views only, so it can show nothing the views
/// would withhold, and it holds no issue keys and no names.
/// </summary>
public static class SprintReport
{
    /// <summary>
    /// What a cell says when the view withholds its figure (ADR-0003): its row has suppressed = 1 and the
    /// figure is null. Such a cell never prints as a blank or a zero.
    /// </summary>
    private const string Suppressed = "suppressed";

    /// <summary>What a baseline cell says when the baseline sprints have no issue in the row's bucket or status.</summary>
    private const string NoBaseline = "none";

    /// <summary>The report's file name for a sprint: reports are named by sprint ID.</summary>
    public static string FileName(string sprintId) => $"sprint-{sprintId}.md";

    /// <summary>
    /// The ID of the sprint that completed last, or null when none has. When a sprint ended is its
    /// sprint window's end, which derive decides.
    /// </summary>
    public static async Task<string?> MostRecentlyCompletedSprintAsync(ITelltaleStore store, CancellationToken cancellationToken = default)
    {
        var sprints = await store.QueryViewsAsync<string>(
            "SELECT sprint_id FROM delivered_per_sprint WHERE state = @closed ORDER BY ended DESC, started DESC, sprint_id DESC LIMIT 1;",
            new { closed = SprintStates.Closed },
            cancellationToken);

        return sprints is [var sprintId] ? sprintId : null;
    }

    /// <summary>
    /// Renders the report. Dates are shown in <paramref name="teamTimeZone"/>. <paramref name="baselineSprintIds"/>
    /// is the baseline sprint list the derived data was built with, in order: when it is not empty, both tables
    /// get baseline columns and the method note says what the baseline is. <paramref name="derivedFrom"/> is
    /// the record of the derived data the report is written from, which the caller reads once from the store
    /// (<see cref="ITelltaleStore.ReadDerivedSourceAsync"/>). The report takes two things from it, as the metric
    /// views do, and decides nothing by either: the minimum group size's numbers the derived data was built
    /// under (<see cref="NumbersInForce.Of"/>), which it states, and whether a status was listed under
    /// <c>briefVisitThresholds</c> (<see cref="DerivedSource.BriefVisitsChecked"/>). With a status listed the
    /// method note explains the footer's brief visit lines, also for a sprint that has none, and the footer
    /// ends with its cycle time line when a listed status has the work role, which the note then explains
    /// too. With no status listed the report is what it was before brief visits were counted, and neither
    /// brief visit view is read.
    /// It also takes the treatment of brief visits the rebuild applied (GLOSSARY: Treatment;
    /// <see cref="DerivedSource.BriefVisitsLeftOutOfTimeInStatus"/>): when brief visits were left out of time in
    /// status, the report says so under its heading, marks the row of every listed status, and words the method
    /// note's paragraph on brief visits accordingly. While both metrics count them, the report is what it was
    /// before a treatment could be chosen, plus the note's sentences on the footer's cycle time line when the
    /// footer has it. When the cycles that start with a brief visit were left out of cycle time
    /// (<see cref="DerivedSource.BriefVisitsLeftOutOfCycleTime"/>), the report says so under its heading, puts
    /// one note under the cycle time table whatever the table holds, says in the footer's cycle time line that
    /// the issues have no cycle time, and words the method note accordingly.
    /// </summary>
    public static async Task<string> RenderAsync(ITelltaleStore store, string sprintId, TimeZoneInfo teamTimeZone, IReadOnlyList<string> baselineSprintIds, DerivedSource derivedFrom, CancellationToken cancellationToken = default)
    {
        var numbers = NumbersInForce.Of(derivedFrom);
        var briefVisitsChecked = derivedFrom.BriefVisitsChecked;

        // The treatment in force is the one the rebuild recorded. Leaving out acts on the listed statuses only,
        // and which rows those are is the brief visit view's to say (ReadTimeInStatusAsync).
        var leftOutOfTimeInStatus = derivedFrom.BriefVisitsLeftOutOfTimeInStatus;
        var leftOutOfCycleTime = derivedFrom.BriefVisitsLeftOutOfCycleTime;

        // Owner decisions D50, D56: while a baseline sprint has its delivered count withheld, every baseline figure is
        // withheld. The baseline views have no rows then, so the cells are written from this list.
        var withheldBaselineSprints = baselineSprintIds.Count == 0 ? [] : await BaselineSprintRules.ReadWithheldAsync(store, cancellationToken);
        var baseline = baselineSprintIds.Count == 0 ? BaselineColumns.None : withheldBaselineSprints.Count > 0 ? BaselineColumns.Withheld : BaselineColumns.Shown;
        var rows = await store.QueryViewsAsync<DeliveredRow>(
            "SELECT sprint_name, started, ended, planned_end, delivered_count, suppressed FROM delivered_per_sprint WHERE sprint_id = @sprintId;",
            new { sprintId },
            cancellationToken);

        if (rows is not [var sprint])
        {
            throw new UserFacingException($"Sprint {sprintId} is not in the store, or has not started. Check the ID against the board's sprints.");
        }

        // Each section is one block of the report, in order. A new section is a new method and a line here.
        var report = new StringBuilder();
        AppendTitle(report, sprint, teamTimeZone);
        AppendNumbersBelowTheDefault(report, numbers);
        AppendBriefVisitsLeftOut(report, leftOutOfTimeInStatus, leftOutOfCycleTime);
        AppendDeliveredCount(report, sprint);
        AppendCycleTime(report, baseline, leftOutOfCycleTime, await store.QueryViewsAsync<CycleTimeRow>(
            """
            SELECT v.point_bucket, v.issue_count, v.median_business_days, v.p85_business_days, v.median_calendar_days, v.p85_calendar_days, v.suppressed,
                   b.median_business_days AS baseline_median_business_days, b.p85_business_days AS baseline_p85_business_days, b.suppressed AS baseline_suppressed
            FROM cycle_time_per_sprint_and_bucket v
            LEFT JOIN baseline_cycle_time_per_bucket b ON b.point_bucket IS v.point_bucket
            WHERE v.sprint_id = @sprintId
            ORDER BY v.point_bucket IS NULL, v.point_bucket;
            """,
            new { sprintId },
            cancellationToken));
        AppendTimeInStatus(report, baseline, await ReadTimeInStatusAsync(store, sprintId, leftOutOfTimeInStatus, cancellationToken), leftOutOfTimeInStatus);

        // The pull request section is there only when the rebuild counted pull requests: without a pull
        // request host configured the view has no rows and is not asked, and the report is what it was.
        if (derivedFrom.PullRequestsCounted)
        {
            AppendPullRequestTiming(report, await store.QueryViewsAsync<PullRequestTimingRow>(
                "SELECT measure, sprint_id IS NULL AS in_no_sprint, host_scope, coverage, sample_count, median_elapsed_days, p85_elapsed_days, median_business_days, p85_business_days FROM pr_timing_per_sprint WHERE sprint_id = @sprintId OR sprint_id IS NULL ORDER BY measure_position, host_scope_position;",
                new { sprintId },
                cancellationToken), derivedFrom.PullRequestHostsCounted);
            AppendPullRequestsWithoutReview(report, await store.QueryViewsAsync<PullRequestsWithoutReviewRow>(
                "SELECT host_scope, coverage, waiting, closed_without_review, no_activity, automation_only, ops_only, unclassifiable, history_not_read FROM pr_without_review_per_sprint WHERE sprint_id = @sprintId ORDER BY host_scope_position;",
                new { sprintId },
                cancellationToken), derivedFrom.PullRequestHostsCounted);
        }

        var quality = await ReadDataQualityAsync(store, cancellationToken);

        // With no status listed neither brief visit view has rows, so neither is asked. The footer's cycle time
        // line is read before the method note is written, because the note explains the line only when the
        // footer has it: the view has a row for the sprint only when a listed status has the work role.
        IReadOnlyList<CycleStartsRow> cycleStarts = briefVisitsChecked
            ? await store.QueryViewsAsync<CycleStartsRow>(
                "SELECT issue_count, suppressed FROM cycle_starts_with_brief_visit_per_sprint WHERE sprint_id = @sprintId;",
                new { sprintId },
                cancellationToken)
            : [];
        var briefVisits = new BriefVisitsInTheReport(briefVisitsChecked, leftOutOfTimeInStatus, leftOutOfCycleTime, HasCycleTimeLine: cycleStarts.Count > 0);
        AppendMethodNote(report, quality, numbers, briefVisits, baselineSprintIds, withheldBaselineSprints);
        AppendFooter(report, quality);

        if (briefVisitsChecked)
        {
            // The number of issues left out of a row is asked for only when brief visits were left out: while
            // they are counted the view has none, and the lines are what they were.
            AppendBriefVisits(report, leftOutOfTimeInStatus, await store.QueryViewsAsync<BriefVisitsRow>(
                leftOutOfTimeInStatus
                    ? "SELECT status_name, threshold_minutes, brief_visit_issue_count, suppressed, left_out_issue_count, left_out_suppressed FROM brief_visits_per_sprint WHERE sprint_id = @sprintId ORDER BY board_position;"
                    : "SELECT status_name, threshold_minutes, brief_visit_issue_count, suppressed FROM brief_visits_per_sprint WHERE sprint_id = @sprintId ORDER BY board_position;",
                new { sprintId },
                cancellationToken));
            AppendCyclesThatStartWithBriefVisit(report, leftOutOfCycleTime, cycleStarts);
        }

        return report.ToString();
    }

    /// <summary>
    /// The rows of the sprint's time in status table. With <paramref name="leftOutOfTimeInStatus"/> a row
    /// says whether it is adjusted: whether its status is one listed for brief visits. That is read from
    /// <c>brief_visits_per_sprint</c>, which has a row for every listed status that has a row in the table,
    /// and is not worked out here from the recorded thresholds: the mark on a row points to the status's
    /// footer line, and the line is written from the same view, so a marked row always has one. While brief
    /// visits are counted no row is adjusted and that view is not read.
    /// </summary>
    private static Task<IReadOnlyList<TimeInStatusRow>> ReadTimeInStatusAsync(ITelltaleStore store, string sprintId, bool leftOutOfTimeInStatus, CancellationToken cancellationToken) =>
        store.QueryViewsAsync<TimeInStatusRow>(
            $"""
            SELECT v.status_name, v.issue_count, v.median_business_days, v.total_business_days, v.suppressed,
                   b.median_business_days AS baseline_median_business_days, b.suppressed AS baseline_suppressed,
                   {(leftOutOfTimeInStatus ? "l.status_id IS NOT NULL" : "0")} AS adjusted
            FROM time_in_status_per_sprint v
            LEFT JOIN baseline_time_in_status b ON b.status_id = v.status_id
            {(leftOutOfTimeInStatus ? "LEFT JOIN brief_visits_per_sprint l ON l.sprint_id = v.sprint_id AND l.status_id = v.status_id" : "")}
            WHERE v.sprint_id = @sprintId
            ORDER BY v.board_position;
            """,
            new { sprintId },
            cancellationToken);

    // A store without this view never gets here: the view-query path refuses derived data another version
    // of the tool built, or a set of views that is not complete (SqliteStore.QueryViewsAsync).
    private static async Task<DataQualityRow> ReadDataQualityAsync(ITelltaleStore store, CancellationToken cancellationToken) =>
        (await store.QueryViewsAsync<DataQualityRow>("SELECT as_of, time_zone, anomaly_issue_count, delivered_between_sprints_count, suppressed FROM data_quality;", cancellationToken: cancellationToken)).Single();

    /// <summary>Renders the report and writes it into <paramref name="directory"/>. Returns the file's path.</summary>
    public static async Task<string> WriteAsync(ITelltaleStore store, string sprintId, TimeZoneInfo teamTimeZone, IReadOnlyList<string> baselineSprintIds, DerivedSource derivedFrom, string directory, CancellationToken cancellationToken = default)
    {
        var text = await RenderAsync(store, sprintId, teamTimeZone, baselineSprintIds, derivedFrom, cancellationToken);

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName(sprintId));
        await File.WriteAllTextAsync(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        return path;
    }

    private static void AppendTitle(StringBuilder report, DeliveredRow sprint, TimeZoneInfo teamTimeZone)
    {
        // The dates are the sprint window's, which the figures were counted in. A sprint that is not
        // completed has no end yet: its window runs to the as-of time. Its planned end is shown as a plan,
        // in words that stay true once that day has passed.
        var start = LocalDate(sprint.Started, teamTimeZone);
        var dates = sprint.Ended is not null ? $"{start} to {LocalDate(sprint.Ended, teamTimeZone)}"
            : sprint.PlannedEnd is null ? $"from {start}, still running"
            : $"from {start}, still running; planned end {LocalDate(sprint.PlannedEnd, teamTimeZone)}";

        report.Append("# ").Append(Markdown.Text(sprint.SprintName)).Append(" (").Append(dates).Append(")\n\n");
    }

    /// <summary>
    /// The line under the heading of a report written with either number below its default (owner decision
    /// D67), so that a report passed on without its context still says what its figures may rest on. A report
    /// under numbers at or above the defaults has no such line. The line gives both numbers, says of each how
    /// it stands to its default, and says what follows from each one that is below.
    /// </summary>
    private static void AppendNumbersBelowTheDefault(StringBuilder report, NumbersInForce numbers)
    {
        var byDefault = MinimumGroupSize.Default;
        var (teamIsBelow, figureIsBelow) = (numbers.Team < byDefault.Team, numbers.BehindAFigure < byDefault.AssigneesBehindAFigure);
        if (!teamIsBelow && !figureIsBelow)
        {
            return;
        }

        if (!numbers.TeamCheck && !numbers.FigureCheck)
        {
            report.Append("Small-group suppression is off: a figure in this report may rest on the work of one or two people.\n\n");
            return;
        }

        // Said of the default's number, so that it is true under every pair of numbers and of every sprint: under
        // the default a sprint with such a team is withheld as a whole, and under a smaller team number one can be
        // reported. Which of its figures are shown is the method note's to say.
        var smallTeam = $"sprint with a team smaller than {NumbersInForce.Number(byDefault.Team)} can be reported";
        const string FewPeople = "figure may rest on the work of fewer people than the default allows";
        report.Append("Minimum group size in this report: ")
            .Append(numbers.TeamCheck ? $"a minimum team of {numbers.TeamInWords}" : "no minimum team")
            .Append(" (").Append(AgainstTheDefault(numbers.Team, byDefault.Team)).Append(") and ")
            .Append(numbers.FigureCheck ? $"a minimum of {numbers.AssigneesBehindAFigure()}" : "no minimum of assignees")
            .Append(" behind a figure (").Append(AgainstTheDefault(numbers.BehindAFigure, byDefault.AssigneesBehindAFigure)).Append("). ")
            .Append(teamIsBelow && figureIsBelow ? $"A {smallTeam}, and a {FewPeople}" : teamIsBelow ? $"A {smallTeam}" : $"A {FewPeople}")
            .Append(".\n\n");
    }

    /// <summary>
    /// The line under the heading of a report whose figures were adjusted by a treatment of brief visits
    /// (GLOSSARY: Treatment; owner decision D86), so that a reader knows before reading a number, as the line
    /// on a lowered minimum group size does (D67). It names each metric that was adjusted, time in status,
    /// cycle time or both, and says of each what was done: what the mark on a time in status row means
    /// (<see cref="AdjustedMark"/>), and that a delivered issue whose cycle starts with a brief visit has no
    /// cycle time (D85). A report under <c>count</c> for both has no such line. The line is there whether or
    /// not anything was left out in this sprint.
    /// </summary>
    private static void AppendBriefVisitsLeftOut(StringBuilder report, bool leftOutOfTimeInStatus, bool leftOutOfCycleTime)
    {
        if (!leftOutOfTimeInStatus && !leftOutOfCycleTime)
        {
            return;
        }

        var both = leftOutOfTimeInStatus && leftOutOfCycleTime;
        report.Append("Brief visits are left out of ")
            .Append(both ? "time in status and of cycle time" : leftOutOfTimeInStatus ? "time in status" : "cycle time")
            .Append(" in this report.");
        if (leftOutOfTimeInStatus)
        {
            report.Append(" A row of ").Append(both ? "the time in status table" : "that table")
                .Append(" marked \"adjusted\" is of a status listed for brief visits: its figures are of what remains, and the footer gives the number of its issues that had a brief visit and the number left out of the row.");
        }

        if (leftOutOfCycleTime)
        {
            report.Append(" A delivered issue whose cycle starts with a brief visit has no cycle time: the cycle time table's figures are of the cycles that remain, and the footer gives the number of such issues.");
        }

        report.Append("\n\n");
    }

    /// <summary>
    /// The note under the cycle time table of a report whose cycles that start with a brief visit were left
    /// out (owner decisions D86 and D101). It points to the footer, where the number of those issues is. It
    /// is there whatever the table holds, also when the table is the single word "suppressed" or says no issue
    /// has a cycle time: a note that depended on the table would tell a count of nothing from a withheld one.
    /// </summary>
    private const string CycleTimeNote = "\nA delivered issue whose cycle starts with a brief visit has no cycle time and is not in this table (adjusted: see the footer).\n";

    /// <summary>
    /// The mark on the time in status row of a status whose brief visits are left out (owner decision D86). It
    /// points to the footer, where the status's brief visit line is. Every listed status's row carries it,
    /// whether or not anything was left out of the row in this sprint: a mark that depended on that would tell
    /// a count of nothing from a withheld one.
    /// </summary>
    private const string AdjustedMark = " (adjusted: see the footer)";

    /// <summary>"below the default of five", "the default", or "above the default of five".</summary>
    private static string AgainstTheDefault(int number, int byDefault) =>
        number == byDefault ? "the default" : $"{(number < byDefault ? "below" : "above")} the default of {NumbersInForce.Number(byDefault)}";

    private static void AppendDeliveredCount(StringBuilder report, DeliveredRow sprint) =>
        report.Append("Delivered issues: ").Append(Figure(sprint.Suppressed, sprint.DeliveredCount?.ToString(CultureInfo.InvariantCulture))).Append('\n');

    /// <summary>
    /// The cycle time section: its heading, the table (<see cref="AppendCycleTimeTable"/>) and, with
    /// <paramref name="leftOutOfCycleTime"/>, <see cref="CycleTimeNote"/> under it, whatever the table holds.
    /// </summary>
    private static void AppendCycleTime(StringBuilder report, BaselineColumns baseline, bool leftOutOfCycleTime, IReadOnlyList<CycleTimeRow> buckets)
    {
        report.Append("\n## Cycle time by point bucket\n\n");
        AppendCycleTimeTable(report, baseline, buckets);
        if (leftOutOfCycleTime)
        {
            report.Append(CycleTimeNote);
        }
    }

    /// <summary>The cycle time table: one row per point bucket, smallest estimate first and unestimated issues last.</summary>
    private static void AppendCycleTimeTable(StringBuilder report, BaselineColumns baseline, IReadOnlyList<CycleTimeRow> buckets)
    {
        if (buckets is [{ IssueCount: null }])
        {
            AppendSuppressedTable(report);
            return;
        }

        if (buckets.Count == 0)
        {
            report.Append("No issue delivered in this sprint has a cycle time.\n");
            return;
        }

        string[] baselineHead = baseline == BaselineColumns.None ? [] : ["Baseline median (business days)", "Baseline P85 (business days)"];
        AppendTableHead(report, ["Point bucket", "Issues with a cycle time", "Median (business days)", "P85 (business days)", "Median (calendar days)", "P85 (calendar days)", .. baselineHead]);
        foreach (var bucket in buckets)
        {
            string[] baselineCells = baseline == BaselineColumns.None
                ? []
                : [BaselineDays(baseline, bucket.BaselineSuppressed, bucket.BaselineMedianBusinessDays), BaselineDays(baseline, bucket.BaselineSuppressed, bucket.BaselineP85BusinessDays)];
            AppendTableRow(report, [BucketLabel(bucket.PointBucket), Count(bucket.IssueCount), Days(bucket.Suppressed, bucket.MedianBusinessDays), Days(bucket.Suppressed, bucket.P85BusinessDays), Days(bucket.Suppressed, bucket.MedianCalendarDays), Days(bucket.Suppressed, bucket.P85CalendarDays), .. baselineCells]);
        }
    }

    /// <summary>
    /// One row per status issues spent time in during the sprint, in board order. A status nobody was in has no row.
    /// A row that is adjusted (<see cref="ReadTimeInStatusAsync"/>) carries <see cref="AdjustedMark"/> after its name.
    /// A table with no row says so in a sentence, worded by <paramref name="leftOutOfTimeInStatus"/>.
    /// </summary>
    private static void AppendTimeInStatus(StringBuilder report, BaselineColumns baseline, IReadOnlyList<TimeInStatusRow> rows, bool leftOutOfTimeInStatus)
    {
        report.Append("\n## Time in status\n\n");
        if (rows is [{ IssueCount: null }])
        {
            AppendSuppressedTable(report);
            return;
        }

        if (rows.Count == 0)
        {
            // With brief visits left out the table can have no row because every issue was left out of every
            // row, and the footer then says how many: "no issue spent time" would be untrue. The sentence is
            // the same whether or not anything was left out, as the mark on a row is.
            report.Append(leftOutOfTimeInStatus
                ? "No issue of this sprint has time in a status that is not done once brief visits are left out (adjusted: see the footer).\n"
                : "No issue of this sprint spent time in a status that is not done.\n");
            return;
        }

        string[] baselineHead = baseline == BaselineColumns.None ? [] : ["Baseline median (business days)"];
        AppendTableHead(report, ["Status", "Issues", "Median (business days)", "Total (business days)", .. baselineHead]);
        foreach (var row in rows)
        {
            string[] baselineCells = baseline == BaselineColumns.None ? [] : [BaselineDays(baseline, row.BaselineSuppressed, row.BaselineMedianBusinessDays)];
            var status = row.Adjusted != 0 ? row.StatusName + AdjustedMark : row.StatusName;
            AppendTableRow(report, [status, Count(row.IssueCount), Days(row.Suppressed, row.MedianBusinessDays), Days(row.Suppressed, row.TotalBusinessDays), .. baselineCells]);
        }
    }

    /// <summary>
    /// How the pull request table names each measure of <c>pr_timing_per_sprint</c>. A measure the view gains
    /// is given its words here; one without an entry is printed under its own name, never left out.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PullRequestMeasureLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [PullRequestTiming.FirstCodeReview] = "First code review",
        [PullRequestTiming.FirstCodeApproval] = "First code approval",
        [PullRequestTiming.Merge] = "Merge",
    };

    /// <summary>
    /// The pull request section's note on Azure DevOps's own rules, written when that host is counted.
    /// </summary>
    private const string AzureDevOpsExplained =
        "- On Azure DevOps, a first code review is the first vote other than a reset, or the first text comment on a file, by someone other than the author, made before the pull request closed. "
        + "A first code approval is the first such vote in favour: approved, or approved with suggestions. A merge is a completion: an abandoned pull request has none, and keeps the reviews it got before it was closed. "
        + "A vote is timed by the record of the vote itself and counted for the reviewer who cast it; a reviewer's current vote has no time and is not used. "
        + "Comments on the pull request as a whole, the author's own comments and votes, and everything from the accounts configured as automation are not code reviews. No Ops group is set apart on Azure DevOps. "
        + "Which accounts are automation is read from the configuration when a comment or vote is first collected, and it keeps that class.\n";

    /// <summary>What a figure's cell says when no pull request has a wait for the row's measure in the sprint: there is no figure.</summary>
    private const string NoWait = "none";

    /// <summary>
    /// The pull request section: one row for each measure the view gives for the sprint, in the view's order,
    /// with the number of pull requests and the median and P85 of their waits on each clock; then the number of
    /// waits the view keeps in no sprint, when there are any; then what the figures are of
    /// (<see cref="AppendPullRequestTimingExplained"/>). It is written only when the derived
    /// data was built with a pull request host configured; a report without one is what it was before.
    /// Nothing in it is withheld for group size (owner decision D114): the view applies neither check, so no
    /// cell here reads "suppressed".
    /// </summary>
    private static void AppendPullRequestTiming(StringBuilder report, IReadOnlyList<PullRequestTimingRow> rows, IReadOnlyList<string> hosts)
    {
        report.Append("\n## Pull request timing\n\n");

        // The view has combined rows only when more than one host is configured, and only then does the table
        // say whose pull requests a row is of: with one host every row is that host's.
        var severalHosts = rows.Any(row => row.HostScope == PullRequestCoverageStates.CombinedScope);
        if (severalHosts)
        {
            AppendTableRow(report, "Wait from opening to", "Host", "Pull requests", "Median (elapsed days)", "P85 (elapsed days)", "Median (business days)", "P85 (business days)");
            report.Append("| --- | --- | ---: | ---: | ---: | ---: | ---: |\n");
        }
        else
        {
            AppendTableHead(report, "Wait from opening to", "Pull requests", "Median (elapsed days)", "P85 (elapsed days)", "Median (business days)", "P85 (business days)");
        }

        // A host's row of a wait whose reviews were not read is not in the table: the line under it names it.
        foreach (var row in rows.Where(row => row.InNoSprint == 0 && row.Coverage != PullRequestCoverageStates.ReviewsNotRead))
        {
            AppendTableRow(
                report,
                [
                    PullRequestMeasureLabel(row),
                    .. severalHosts ? [HostScopeLabel(row)] : Array.Empty<string>(),
                    row.SampleCount?.ToString(CultureInfo.InvariantCulture) ?? NotKnown,
                    WaitDays(row, row.MedianElapsedDays),
                    WaitDays(row, row.P85ElapsedDays),
                    WaitDays(row, row.MedianBusinessDays),
                    WaitDays(row, row.P85BusinessDays),
                ]);
        }

        // The waits the view keeps in no sprint are said by their number alone, and only when there are any:
        // they are not this sprint's, so their figures are not put beside its own. With several hosts it is the
        // number of all of them together.
        var inNoSprint = rows.Where(row => row.InNoSprint != 0 && row.SampleCount > 0 && (!severalHosts || row.HostScope == PullRequestCoverageStates.CombinedScope))
            .Select(row => $"{PullRequestMeasureLabel(row)} {row.SampleCount?.ToString(CultureInfo.InvariantCulture)}")
            .ToList();
        if (inNoSprint.Count > 0)
        {
            report.Append("\nIn no sprint's row, of everything collected, because the event fell between sprint windows or in more than one: ")
                .AppendJoin(", ", inNoSprint)
                .Append(".\n");
        }

        report.Append('\n');
        var sprintRows = rows.Where(row => row.InNoSprint == 0).ToList();
        AppendPullRequestMeasuresNotShown(report, sprintRows, severalHosts);
        AppendPullRequestHostsNotRead(report, sprintRows, severalHosts);
        AppendPullRequestHostsCollectedInPart(report, sprintRows, severalHosts);
        if (severalHosts)
        {
            report.Append("- Each host has its own rows, and a combined row holds the waits of every host together: its median and P85 are taken of those waits, never of the hosts' medians or P85s.\n");
        }

        AppendPullRequestTimingExplained(report, hosts);
    }

    /// <summary>The words for a row's measure, or the measure's own name when it has none yet.</summary>
    private static string PullRequestMeasureLabel(PullRequestTimingRow row) => PullRequestMeasureLabels.GetValueOrDefault(row.Measure, row.Measure);

    /// <summary>How the report names each pull request host. A host without an entry is printed under its own name.</summary>
    private static readonly IReadOnlyDictionary<string, string> PullRequestHostLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [PullRequestHosts.GitHub] = "GitHub",
        [PullRequestHosts.Ado] = "Azure DevOps",
        [PullRequestCoverageStates.CombinedScope] = "Combined",
    };

    /// <summary>The words for a host, or for the combined scope.</summary>
    private static string PullRequestHostLabel(string hostScope) => PullRequestHostLabels.GetValueOrDefault(hostScope, hostScope);

    /// <summary>
    /// The host cell of a row: whose pull requests it is of, and, when the view says the row does not rest on
    /// a full read, that it does not. The label is the view's <c>coverage</c> in words: the report decides
    /// nothing about coverage itself.
    /// </summary>
    private static string HostScopeLabel(PullRequestTimingRow row) => HostScopeLabel(row.HostScope, row.Coverage);

    /// <summary>The same cell for any view's row that has the two columns.</summary>
    private static string HostScopeLabel(string hostScope, string coverage) => PullRequestHostLabel(hostScope) + coverage switch
    {
        PullRequestCoverageStates.FailedRead => " (last read failed)",
        PullRequestCoverageStates.NotRead => " (not read yet)",
        PullRequestCoverageStates.Partial => " (partial)",
        _ => "",
    };

    /// <summary>What a cell says when the view gives no count: the store does not hold what the row would count, and that is not zero.</summary>
    private const string NotKnown = "not known";

    /// <summary>
    /// A pull request figure as the table shows it: rounded to one decimal here and nowhere earlier,
    /// <see cref="NoWait"/> when the row counts no pull request, or <see cref="NotKnown"/> when the row has no count.
    /// </summary>
    private static string WaitDays(PullRequestTimingRow row, double? days) =>
        days?.ToString("F1", CultureInfo.InvariantCulture) ?? (row.SampleCount is null ? NotKnown : NoWait);

    /// <summary>
    /// The line for the waits a host has no row for in the table, by the reason the view gives
    /// (<c>coverage</c> is "reviews not read"): the Ops group's members could not be read, so no review of
    /// that host was collected. With several hosts the line names the host, and says what that makes of the
    /// combined rows. Nothing is written when no row has that reason.
    /// </summary>
    private static void AppendPullRequestMeasuresNotShown(StringBuilder report, IReadOnlyList<PullRequestTimingRow> sprintRows, bool severalHosts)
    {
        foreach (var host in sprintRows.Where(row => row.Coverage == PullRequestCoverageStates.ReviewsNotRead).GroupBy(row => row.HostScope))
        {
            report.Append(severalHosts ? $"- Not shown for {PullRequestHostLabel(host.Key)}: " : "- Not shown: ")
                .AppendJoin(", ", host.Select(PullRequestMeasureLabel))
                .Append(". The members of the Ops group could not be read when the pull requests were last collected, so reviews by the group could not be told apart from code reviews, and no review was collected. These waits are given for no sprint until a collection reads the group; nothing here says that no pull request was reviewed or approved. A merge does not depend on who reviewed, and is shown.")
                .Append(severalHosts ? " The combined rows of these waits are marked partial: they hold the other hosts' waits only." : "")
                .Append('\n');
        }
    }

    /// <summary>
    /// One line for each host whose rows do not rest on a finished read, by the reason the view gives: its
    /// last collection failed, or it is configured and was never read. Either way the line says that a row
    /// without a count is not a row that counts nothing. A host that is not configured has no row and no line.
    /// </summary>
    private static void AppendPullRequestHostsNotRead(StringBuilder report, IReadOnlyList<PullRequestTimingRow> sprintRows, bool severalHosts)
    {
        foreach (var host in sprintRows.Where(row => row.Coverage is PullRequestCoverageStates.FailedRead or PullRequestCoverageStates.NotRead).GroupBy(row => (row.HostScope, row.Coverage)))
        {
            var name = PullRequestHostLabel(host.Key.HostScope);
            report.Append(host.Key.Coverage == PullRequestCoverageStates.FailedRead
                    ? $"- {name} could not be read when pull requests were last collected. Its figures are of what earlier collections read, so a count may be short of what {name} has now, and where they read nothing the row says \"not known\": that is not a count of zero."
                    : $"- {name} is configured and has not been read yet: no collection of it has finished. Its rows say \"not known\": that is not a count of zero.")
                .Append(severalHosts ? $" The combined rows are marked partial: they leave out whatever a read of {name} would add." : "")
                .Append('\n');
        }
    }

    /// <summary>
    /// One line for each host whose own rows the view marks partial: the host was read, and the sprint began
    /// before the start date its pull requests were collected under, so a pull request last active before
    /// that date is not in the store. What "last active" means is the host's: GitHub's date bounds the last
    /// update, Azure DevOps's the closing. The line says that a count may be short and that a row without a
    /// count is not a row that counts nothing. A combined row is partial for its hosts' reasons and has no line.
    /// </summary>
    private static void AppendPullRequestHostsCollectedInPart(StringBuilder report, IReadOnlyList<PullRequestTimingRow> sprintRows, bool severalHosts)
    {
        foreach (var host in sprintRows.Where(row => row.Coverage == PullRequestCoverageStates.Partial && row.HostScope != PullRequestCoverageStates.CombinedScope).Select(row => row.HostScope).Distinct())
        {
            var name = PullRequestHostLabel(host);
            report.Append($"- {name}'s pull requests are collected from a start date (collectFrom), and this sprint began before it: a pull request {(host == PullRequestHosts.Ado ? "closed" : "last updated")} before that date is not in the store. ")
                .Append($"{name}'s rows hold only what was collected, so a count may be short of what {name} has, and where nothing was collected a row says \"not known\": that is not a count of zero.")
                .Append(severalHosts ? $" The combined rows are marked partial: they leave out whatever {name} has from before that date." : "")
                .Append('\n');
        }
    }

    /// <summary>
    /// The pull request section's own notes: what the population is, where a wait starts, ends and is counted,
    /// what is not a code review, and that the minimum group size does not apply here. They stand under the
    /// table and not in the method note, so a report without the section has nothing of them.
    /// </summary>
    /// <param name="hosts">The hosts the figures are of: a host's own rules are worded only when it is one of them.</param>
    private static void AppendPullRequestTimingExplained(StringBuilder report, IReadOnlyList<string> hosts)
    {
        report
            .Append("- These are the pull requests of the configured repositories: every one of them, linked to a Jira issue or not, whoever opened it. They are not necessarily this team's pull requests, and no team is inferred from them.\n")
            .Append("- Each wait runs from the moment the pull request was first opened to the event named, and is counted once, at its full length, in the sprint whose window holds that event: a review in this sprint of a pull request opened two sprints ago counts here, with the whole wait. An event between sprint windows, or in more than one, is counted in no sprint. That is not the rule for delivered issues above, where a delivery in overlapping sprints goes to the one that started first.\n")
            .Append("- Elapsed days are the wait divided by 24 hours, weekends and holidays included. Business days are the same wait counted only on days that are neither a weekend nor a company holiday, in the team's time zone. The median is the middle wait, or the mean of the two middle ones; the P85 is the smallest wait with at least 85% of the waits at or below it (nearest rank). Each clock is ranked on its own, from the waits before any rounding, so its figures need not come from the same pull requests as the other clock's. \"Pull requests\" is the number of pull requests with a wait counted in the sprint, the same for both clocks. A pull request that has no such event yet is not in the row: it has no wait, not a wait of zero, and a row with no pull request has no figure. The figures are recorded waits, not the effort a review took.\n")
            .Append(!hosts.Contains(PullRequestHosts.GitHub) ? "" : "- A first code review is the first submitted review by someone other than the author: a comment-only review, a request for changes or an approval, also one dismissed later, made before the pull request closed. Reviews by the author, by automation and by the Ops group are not code reviews, and neither are ordinary comments. Who is in the Ops group is read when a review is first collected, so a review collected long after it was made is judged by the group as it was at that collection, not as it was when the review was made.\n")
            .Append(!hosts.Contains(PullRequestHosts.GitHub) ? "" : "- A first code approval is the first approving review by someone other than the author, made before the pull request closed. When it is also the first review, it ends both waits. A comment-only review and a request for changes are code reviews and not approvals. An approval by the Ops group is kept apart and is never a code approval, and neither is one by the author or by automation. A review that had been dismissed before it was first collected does not say what it was: it is a code review and no approval, and since it may have been one, a pull request with such a review before its first known approval has no first code approval here. An approval that was collected and dismissed afterwards stays an approval. As for code reviews, the Ops group is the one read when a review is first collected: its members today, not those at the time of an older review.\n")
            .Append(!hosts.Contains(PullRequestHosts.Ado) ? "" : AzureDevOpsExplained)
            .Append("- Pull request figures are not subject to the minimum group size: none is withheld for the number of people behind it, and none is reported per person.\n");
    }

    /// <summary>
    /// The pull requests that give the sprint no first code review, under the pull request section: one row
    /// for each host scope the view gives (each host, and the combined one when there are several), named and
    /// marked as in the table of waits, then what each column counts. These explain the number of pull
    /// requests in the first code review row and are apart from it: a count here is never a wait. A number
    /// the view does not give, because the row does not rest on a full read, is "not known" and never zero.
    /// Like the figures above they are not withheld for group size (owner decision D114).
    /// </summary>
    private static void AppendPullRequestsWithoutReview(StringBuilder report, IReadOnlyList<PullRequestsWithoutReviewRow> rows, IReadOnlyList<string> hosts)
    {
        report.Append("\n### Without a first code review\n\n")
            .Append("Pull requests of the configured repositories that had no first code review when the sprint closed, or at the as-of time for a sprint that is still running. ")
            .Append("These are numbers of pull requests, not waits: none of them is in the first code review row above, and none is counted as a wait of zero.\n\n");
        AppendTableHead(report, "Pull requests of", "Still waiting", "Closed without one", "No activity", "Automation only", "Ops only", "Cannot be classified", "History not read");
        foreach (var row in rows)
        {
            AppendTableRow(
                report,
                HostScopeLabel(row.HostScope, row.Coverage),
                Number(row.Waiting),
                Number(row.ClosedWithoutReview),
                Number(row.NoActivity),
                Number(row.AutomationOnly),
                row.HostScope == PullRequestHosts.Ado ? "no Ops group" : Number(row.OpsOnly),
                Number(row.Unclassifiable),
                Number(row.HistoryNotRead));
        }

        var someNotKnown = rows.Any(row => row.Waiting is null || row.ClosedWithoutReview is null || row.Unclassifiable is null || row.HistoryNotRead is null);
        report.Append(!someNotKnown ? "\n" : "\n- \"not known\" stands where a row does not rest on a full read of its reviews (the lines under the table above say which and why) and the store holds no such pull request: that is not a count of zero.\n");
        report
            .Append("- Still waiting: open at that moment with no first code review yet. A review made later does not take a pull request out of this sprint's number: it was waiting when the sprint closed, and its wait is counted in the sprint of the review.\n")
            .Append("- Closed without one: closed during the sprint, merged or not, with no first code review before it closed. A merge is still counted in the Merge row.\n")
            .Append("- No activity, Automation only and Ops only split the pull requests of the first two columns by what had been submitted on each before it closed. Each of those pull requests is in exactly one of the three, so the three add up to the first two columns and are not further pull requests. ")
            .Append("Ops only: something by the Ops group, whatever automation did besides").Append(hosts.Contains(PullRequestHosts.Ado) ? "; Azure DevOps has no Ops group set apart" : "")
            .Append(". Automation only: something by automation and nothing by the Ops group. No activity: nothing, or only the author's own; the pull request's history was read and holds nothing else.\n")
            .Append("- Cannot be classified: something that could have been the first code review cannot be told to be one or not, so the pull request has no first code review that can be relied on. It is in none of the columns before.\n")
            .Append("- History not read: the pull request's reviews could not be read when it was last collected, so it cannot be said whether it was reviewed. That is not a history that was read and found empty, and the pull request is in none of the columns before.\n")
            .Append("- Like the figures above, these numbers are not subject to the minimum group size, and none is of a person.\n");
    }

    /// <summary>A number of pull requests, zero too, or <see cref="NotKnown"/> when the view gives none.</summary>
    private static string Number(long? pullRequests) => pullRequests?.ToString(CultureInfo.InvariantCulture) ?? NotKnown;

    /// <summary>
    /// Writes the one line that stands for a table its view withholds as a whole: no row, and no sentence about
    /// an empty table either, because which buckets or statuses have rows says what the sprint's few people
    /// worked on (owner decision D45). The view decides, and decides once: for such a sprint it gives exactly
    /// one row, with no count (D41, D54), and the table's caller writes this line when that is what it got.
    /// </summary>
    private static void AppendSuppressedTable(StringBuilder report) => report.Append(Suppressed).Append('\n');

    /// <summary>
    /// What the figures mean, with each rule as the glossary and the owner's decisions state it. The suppression
    /// rule is ADR-0003's for results grouped by issue attributes, stated with the numbers the derived data was
    /// built under (<see cref="AppendMinimumGroupSize"/>). The time zone is the one derive stored. The paragraph
    /// on brief visits is its last, and is there only when a status is listed (<see cref="AppendBriefVisitsExplained"/>).
    /// </summary>
    private static void AppendMethodNote(StringBuilder report, DataQualityRow quality, NumbersInForce numbers, BriefVisitsInTheReport briefVisits, IReadOnlyList<string> baselineSprintIds, IReadOnlyList<string> withheldBaselineSprints)
    {
        var (briefVisitsChecked, leftOutOfTimeInStatus, leftOutOfCycleTime, _) = briefVisits;
        report.Append("\n## Method\n\n")
            .Append("- Durations are in business days: days that are neither a weekend nor a company holiday, each counted in full (24 hours) in the team's time zone (").Append(quality.TimeZone).Append("). Calendar days are shown alongside: elapsed time divided by 24 hours.\n")
            .Append("- Cycle time runs from an issue's first entry into a work status to its last entry into a done status, for delivered issues only. It belongs to the sprint in which the issue was delivered.")

            // Under exclude for cycle time not every delivered issue that entered a work status has one (owner decision D85).
            .Append(leftOutOfCycleTime ? " A delivered issue whose cycle starts with a brief visit has no cycle time (see the paragraph on brief visits below).\n" : "\n")
            .Append("- The median is the middle value, or the mean of the two middle values. The P85 is the smallest value with at least 85% of the values at or below it.\n")
            .Append("- Time in status includes delivered and unfinished issues in statuses before done, clipped to the sprint window and each issue's membership intervals. An issue counts toward a status only if it spent more than zero business days there. Total business days sum that time across all contributing issues, including repeated visits")

            // Under exclude a repeated visit that is brief is not in the total, so the sentence says so (owner decision D84).
            .Append(leftOutOfTimeInStatus ? " and leaving out brief visits to a status listed for them (see the paragraph on brief visits below)" : "")
            .Append("; this is accumulated issue time, not effort or the sprint's elapsed duration. Totals and medians use the same privacy rules.\n");
        AppendMinimumGroupSize(report, numbers, briefVisitsChecked, leftOutOfTimeInStatus);

        // Said only when there is a baseline: without one the report has no baseline column to explain.
        if (baselineSprintIds.Count > 0)
        {
            report.Append("- The baseline columns pool every issue of the baseline sprints, a list fixed in the configuration (sprints ").AppendJoin(", ", baselineSprintIds)
                .Append("), each a completed sprint")

                // Under exclude for either metric the pool is of what remains, as the sprint's own figures are (owner
                // decisions D83 to D85: a treatment applies to the baseline sprints too).
                .Append(leftOutOfTimeInStatus || leftOutOfCycleTime ? ", with brief visits left out as they are in this report's own figures (see the paragraph on brief visits below). " : ". ")
                .Append(WhenABaselineFigureIsNotShown(numbers)).Append('\n');
        }

        if (withheldBaselineSprints.Count > 0)
        {
            report.Append("- ").Append(BaselineSprintRules.WithheldInTheReport(withheldBaselineSprints, numbers)).Append('\n');
        }

        report.Append("- An interval that is still open (an issue still in a status, a sprint not yet completed) ends at the as-of time below.\n")
            .Append("- Work that has since been removed from Jira stays in the figures until the next backfill.\n");
        if (briefVisitsChecked)
        {
            AppendBriefVisitsExplained(report, numbers, briefVisits);
        }
    }

    /// <summary>
    /// What the report was told about brief visits by the record of the derived data and by the views, for the
    /// method note: whether a status is listed, whether brief visits were left out of time in status and of
    /// cycle time (GLOSSARY: Treatment), and whether the footer has its cycle time line, which it has when a
    /// listed status has the work role.
    /// </summary>
    private readonly record struct BriefVisitsInTheReport(bool Checked, bool LeftOutOfTimeInStatus, bool LeftOutOfCycleTime, bool HasCycleTimeLine);

    /// <summary>
    /// The method note's paragraph on brief visits (GLOSSARY: Brief visit, Treatment; owner decisions D60 to
    /// D62 and D83 to D89), written only when a status is listed. It says what a brief visit is, what the
    /// footer's lines count, what is done with brief visits, what a count reads in place of a number, which
    /// statuses have a line, and when each rule was added.
    /// While both metrics count brief visits the paragraph is what it was before a treatment could be chosen,
    /// plus the sentences on the footer's cycle time line when the footer has it
    /// (<see cref="AppendCycleTimeLineExplained"/>). When either metric leaves them out, the sentence that
    /// brief visits are counted and nothing more gives way to <see cref="AppendTreatmentExplained"/>, and the
    /// paragraph ends by saying that leaving out was added on 2026-11-16, after results had been seen (D89).
    /// With brief visits left out of time in status (D84, D93, D99), three more sentences have another
    /// wording, because each would be untrue: which row the count is measured against (the one before
    /// anything was left out), which row's check withholds a count, and that a status whose every issue was
    /// left out has a line and no row. The paragraph then also says that a listed status's line carries a
    /// second number, the issues left out of the row, and what that number reads when it is withheld.
    /// What a count reads follows the numbers in force (D67), as the views do (<c>brief_visits_per_sprint</c>):
    /// while a figure needs assignees behind it, a count of nothing has nobody behind it and is withheld as a
    /// count with too few is; with that number at zero no count is withheld and a count of nothing is printed
    /// as 0. A sprint short of a team has no line for a listed status, because the view has no row for it,
    /// and still has the cycle time line.
    /// </summary>
    private static void AppendBriefVisitsExplained(StringBuilder report, NumbersInForce numbers, BriefVisitsInTheReport briefVisits)
    {
        var (_, leftOutOfTimeInStatus, leftOutOfCycleTime, hasCycleTimeLine) = briefVisits;
        report.Append("- A brief visit is one unbroken stay in a status that ended and was shorter than the status's threshold.")
            .Append(" It is measured in clock time over the issue's whole history: weekends and holidays count, and the stay is not clipped to the sprint.")
            .Append(leftOutOfTimeInStatus
                ? " For each status the configuration file lists (briefVisitThresholds), the footer gives how many of the issues that were in the status's time in status row before anything was left out had a brief visit during the time the row counts."
                : " For each status the configuration file lists (briefVisitThresholds), the footer gives how many of the issues in the status's time in status row had a brief visit during the time the row counts.");
        if (leftOutOfTimeInStatus || leftOutOfCycleTime)
        {
            AppendTreatmentExplained(report, leftOutOfTimeInStatus, leftOutOfCycleTime);
        }
        else
        {
            report.Append(" Brief visits are counted and nothing more: no duration was excluded or corrected because of one, and every other figure is what it would be without the count.");
        }

        report.Append(" A short median in the time in status table does not show how many issues had a brief visit.");
        if (numbers.FigureCheck)
        {
            report.Append(leftOutOfTimeInStatus
                    ? " A count reads \"suppressed\" when its row in the time in status table would read \"suppressed\" for its figures with nothing left out, or when fewer than "
                    : " A count reads \"suppressed\" when its row in the time in status table reads \"suppressed\" for its figures, or when fewer than ")
                .Append(numbers.AssigneesBehindAFigure(distinct: true)).Append(' ').Append(numbers.BehindAFigureAre).Append(" among the issues counted.")
                .Append(" A count of nothing has nobody behind it, so a zero and a withheld count both read \"suppressed\".");
        }
        else
        {
            report.Append(numbers.TeamCheck ? " No count is withheld for the number of assignees behind it" : " No count is withheld for group size")
                .Append(", and a count of nothing reads 0.");
        }

        // The second number of a listed status's line is there only when brief visits are left out of time in
        // status. The view withholds it by the same check and the same number as the count.
        if (leftOutOfTimeInStatus)
        {
            report.Append(" A listed status's line goes on to a second number: how many of the issues with a brief visit had no other business time in the status and so are not in the row's count.")
                .Append(numbers.FigureCheck
                    ? $" It reads \"suppressed\" when fewer than {numbers.AssigneesBehindAFigure(distinct: true)} {numbers.BehindAFigureAre} among the issues left out, and so also when nobody was left out."
                    : " It is not withheld either, and reads 0 when nobody was left out.");
        }

        report.Append(leftOutOfTimeInStatus
                ? " A listed status that had no row in the time in status table before anything was left out has no line; one whose every issue was left out has a line and no row"
                : " A listed status with no row in the time in status table has no line")
            .Append(numbers.TeamCheck ? $", and a sprint whose team is smaller than {numbers.TeamInWords} has no line for any listed status." : ".");
        if (hasCycleTimeLine)
        {
            AppendCycleTimeLineExplained(report, numbers, leftOutOfCycleTime);
        }

        report.Append(" This rule was added on 2026-11-12, after the first run on real data: the thresholds were chosen with knowledge of results, and the counts describe data quality and are not a success criterion.");
        if (leftOutOfTimeInStatus || leftOutOfCycleTime)
        {
            report.Append(" Leaving brief visits out was added on 2026-11-16, after results had been seen, and it changes how ")
                .Append(leftOutOfTimeInStatus && leftOutOfCycleTime ? "time in status and cycle time are" : leftOutOfTimeInStatus ? "time in status is" : "cycle time is")
                .Append(" measured.");
        }

        report.Append('\n');
    }

    /// <summary>
    /// The sentences of the paragraph on brief visits that are there only when brief visits are left out of a
    /// metric (GLOSSARY: Treatment), in place of the sentence that they are counted and nothing more: the
    /// treatment in force for each of the two metrics, what was left out (owner decisions D84 and D85), why,
    /// and what leaving out does not do. Nothing here depends on the sprint or on the numbers in force.
    /// The reason given for a cycle is that it starts when the board was updated and not when the work
    /// started (owner decision D107). That usually makes it too short; a move into a work status and straight
    /// back out before the work began (D91) makes it too long, so the report does not say too short alone.
    /// </summary>
    private static void AppendTreatmentExplained(StringBuilder report, bool leftOutOfTimeInStatus, bool leftOutOfCycleTime)
    {
        report.Append(" What is done with brief visits is set for time in status and for cycle time separately (briefVisitTreatment in the configuration file). In this report they are ")
            .Append(leftOutOfTimeInStatus && leftOutOfCycleTime ? "left out of time in status and of cycle time"
                : leftOutOfTimeInStatus ? "left out of time in status and counted for cycle time"
                : "counted for time in status and left out of cycle time")
            .Append('.');
        if (leftOutOfTimeInStatus)
        {
            report.Append(" The time of every brief visit to a listed status is left out of the issue's time in that status, and an issue left with no business time in the status is not in its row: the row's count, median and total, and the baseline median, are of what remains.");
        }

        if (leftOutOfCycleTime)
        {
            report.Append(" A delivered issue whose cycle starts with a brief visit has no cycle time. It is still delivered: the delivered count does not change, and the cycle time table's counts, medians and P85s, and the baseline's, are of the cycles that remain.");
        }

        report.Append(" The reason: a stay shorter than the threshold is too short to be the work, and its usual cause is an issue moved through several statuses at once after the work was done, so its recorded length says when the board was updated and not how long the work took.");
        if (leftOutOfCycleTime)
        {
            report.Append(" A cycle that starts with such a stay starts when the board was updated, not when the work began.");
        }

        report.Append(" Leaving out is not a correction: the time the work took is still recorded under the earlier status, the adjusted figures describe the issues that were moved on time, which may not be typical, and no length was estimated or replaced.")
            .Append(" No other figure was changed because of a brief visit.");
    }

    /// <summary>
    /// The paragraph's sentences on the footer's cycle time line (owner decisions D87 and D100), written only
    /// when the footer has the line: what the line counts, whether the issues it counts are in the cycle time
    /// figures, what the number reads in place of a number, and that the line was added on 2026-11-16,
    /// so that the date the paragraph gives next, for the rule on brief visits, is not read as the line's.
    /// The sentences are there under either treatment, and the date is the day the line was decided (D87),
    /// as the date given for the rule on brief visits is the day that rule was decided (owner decision D109).
    /// What the number reads follows the numbers in force, as the view
    /// does (<c>cycle_starts_with_brief_visit_per_sprint</c>): the number is withheld when the sprint's
    /// delivered count is, which holds the team check, so a sprint short of a team has the line and it reads
    /// "suppressed"; while a figure needs assignees behind it, the number is also withheld when too few are
    /// behind the issues it counts, and a number of nothing with it.
    /// </summary>
    private static void AppendCycleTimeLineExplained(StringBuilder report, NumbersInForce numbers, bool leftOutOfCycleTime)
    {
        report.Append(" The footer's last line gives how many of the sprint's delivered issues have a cycle that starts with a brief visit: the stay that begins where the cycle starts is one.")
            .Append(leftOutOfCycleTime ? " Those issues have no cycle time." : " Those issues are in the cycle time figures.");
        if (!numbers.TeamCheck && !numbers.FigureCheck)
        {
            report.Append(" Its number is not withheld, and a number of nothing reads 0.");
        }
        else
        {
            report.Append(" Its number reads \"suppressed\" when the sprint's delivered count does")
                .Append(numbers.TeamCheck ? $", as for a sprint whose team is smaller than {numbers.TeamInWords}" : "")
                .Append(numbers.FigureCheck
                    ? $", when fewer than {numbers.AssigneesBehindAFigure(distinct: true)} {numbers.BehindAFigureAre} among the issues it counts, and when it is nothing."
                    : ", and a number of nothing reads 0.");
        }

        // The sentence that follows these in the paragraph dates the rule on brief visits, which is older.
        report.Append(" This line was added on 2026-11-16, after results had been seen.");
    }

    /// <summary>
    /// The method note's paragraphs on the minimum group size and on who counts as an assignee, with the numbers
    /// in force (owner decision D67). Each sentence is about one of the two checks and is written only while
    /// that check applies: a number of zero turns its check off (D65). With both off, the two paragraphs give
    /// way to one that says so. With a status listed for brief visits the footer can have brief visit lines
    /// after its two counts of the whole store, withheld by their own rule (<see cref="AppendBriefVisitsExplained"/>),
    /// so the sentence on those two says which they are, whether or not this sprint has such a line.
    /// </summary>
    private static void AppendMinimumGroupSize(StringBuilder report, NumbersInForce numbers, bool briefVisitsChecked, bool leftOutOfTimeInStatus)
    {
        if (!numbers.TeamCheck && !numbers.FigureCheck)
        {
            report.Append("- No figure in this report was withheld for group size: small-group suppression is off (minimumGroupSize in the configuration file). Person fields are still hashed before they are stored.\n");
            return;
        }

        var team = numbers.TeamInWords;
        var behindAFigure = numbers.AssigneesBehindAFigure(distinct: true);
        var are = numbers.BehindAFigureAre;

        // One assignee behind a figure is one person's work, so the opening sentence is said only from two.
        report.Append(numbers.BehindAFigure >= 2
            ? "- A figure is shown only where it cannot show one person's work."
            : "- A figure is shown only for a group of at least the minimum group size.");
        if (numbers.TeamCheck)
        {
            report.Append(" The team of a sprint is the distinct assignees of every issue that was a member of the sprint or was delivered in it. When the team is smaller than ").Append(team)
                .Append(", the delivered count and both tables read \"suppressed\", with no rows.");
        }

        if (numbers.FigureCheck)
        {
            report.Append(numbers.TeamCheck ? $" With a team of {team} or more, a row" : " A row")
                .Append(" with fewer than ").Append(behindAFigure).Append(" behind it keeps its issue count and reads \"suppressed\" for its figures, and the delivered count reads \"suppressed\" when fewer than ")
                .Append(behindAFigure).Append(' ').Append(are).Append(" among the delivered issues, and the cycle time table then reads \"suppressed\" as well, with no rows, because the number of its rows and their counts (the delivered issues that have a cycle time) would each give a lower bound of the withheld count. The time in status table of such a sprint keeps its counts: they count every issue that spent time in a status, delivered or not, ")

                // Under exclude an issue whose only time in a listed status was brief visits is not in its count (owner decision D84).
                .Append(leftOutOfTimeInStatus ? "apart from an issue whose only time in a status listed for brief visits was brief visits, which is not in that status's count, " : "")
                .Append("so the count of a late status can be close to the withheld delivered count.");
        }
        else
        {
            // Of the sprint itself: a baseline figure beside it can still be withheld, for a baseline sprint's team.
            report.Append(" With a team of ").Append(team).Append(" or more, every figure of the sprint itself is shown, whatever the number of assignees behind it.");
        }

        report.Append(briefVisitsChecked ? " The footer's two counts of the whole store" : " The footer's two counts").Append(" read \"suppressed\" when ");
        if (numbers.TeamCheck)
        {
            report.Append("the issues in the whole store have fewer than ").Append(numbers.TeamAssignees(distinct: true))
                .Append(numbers.FigureCheck ? $", or fewer than {numbers.BehindAFigureInWords} {are} among the issues counted." : ".");
        }
        else
        {
            report.Append("fewer than ").Append(behindAFigure).Append(' ').Append(are).Append(" among the issues counted. No figure is withheld for the size of a sprint's team.");
        }

        report.Append("\n- An assignee is a Jira account as of the last sync, so a bot or a person's second account counts as a person. An unassigned issue counts toward nobody.\n");
    }

    /// <summary>
    /// The baseline paragraph's sentences on what a baseline cell reads in place of a figure (owner decisions
    /// D44, D50, D56), with the numbers in force. A row is suppressed in a sprint's own report only by the check
    /// on the assignees behind a figure, so the sentence about such a row is said only while that check applies.
    /// With both checks off no baseline figure is withheld, and only the sentence about a row the baseline lacks
    /// is left.
    /// </summary>
    private static string WhenABaselineFigureIsNotShown(NumbersInForce numbers)
    {
        const string None = "reads \"none\" when no issue of the baseline sprints is in the row.";
        if (!numbers.TeamCheck && !numbers.FigureCheck)
        {
            return $"A baseline figure {None}";
        }

        var teamIsSmall = $"its team is smaller than {numbers.TeamInWords}";
        var fewDelivered = $"fewer than {numbers.AssigneesBehindAFigure(distinct: true)} {numbers.BehindAFigureAre} among its delivered issues";
        return (numbers.FigureCheck
                ? $"A baseline figure reads \"suppressed\" when any baseline sprint has that row suppressed in its own report, so that a pooled figure cannot give back a withheld one. It {None}"
                : $"A baseline figure {None}")
            + $" Every baseline figure reads \"suppressed\" when a baseline sprint has its delivered count withheld: {(numbers.TeamCheck && numbers.FigureCheck ? $"{teamIsSmall}, or {fewDelivered}" : numbers.TeamCheck ? teamIsSmall : fewDelivered)}.";
    }

    /// <summary>
    /// The data-quality footer, up to its brief visit lines (<see cref="AppendBriefVisits"/>). Its two counts
    /// here cover the whole store, not the sprint: an issue delivered between sprints belongs to no sprint.
    /// They are counts only: no issue is named. The view withholds them when the whole store has fewer distinct
    /// assignees than a team (five by default), or fewer than a figure needs (three by default) are among the
    /// issues a count covers.
    /// </summary>
    private static void AppendFooter(StringBuilder report, DataQualityRow quality)
    {
        // In the zone derive counted business days in, which is the one the note names.
        var asOf = TimeZoneInfo.ConvertTime(StoredTime.FromStored(quality.AsOf), TimeZoneInfo.FindSystemTimeZoneById(quality.TimeZone));
        report.Append("\n---\n\n")
            .Append("- As-of time: ").Append(asOf.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)).Append(" (").Append(quality.TimeZone).Append(")\n")
            .Append("- Issues with changelog anomalies, in the whole store: ").Append(Figure(quality.Suppressed, quality.AnomalyIssueCount?.ToString(CultureInfo.InvariantCulture))).Append('\n')
            .Append("- Issues delivered between sprints, in the whole store: ").Append(Figure(quality.Suppressed, quality.DeliveredBetweenSprintsCount?.ToString(CultureInfo.InvariantCulture))).Append('\n');
    }

    /// <summary>
    /// The footer's brief visit lines (GLOSSARY: Brief visit; owner decision D61), after its other lines: one
    /// per row of the view, so one per listed status that has a row in the sprint's time in status table, in
    /// board order; when brief visits are left out of time in status, one per listed status that had a row
    /// before anything was left out (owner decision D93). A line names the status and states its threshold. The view withholds a count as it does
    /// every figure, and a count of nothing with it, so the two read alike.
    /// With <paramref name="leftOutOfTimeInStatus"/> a line goes on to say that those visits are left out of
    /// the status's row, and how many of the issues had no other business time in the status and so are not in the
    /// row's count (GLOSSARY: Treatment; owner decisions D84, D86). The view withholds that number by the same
    /// check, and a number of nothing with it. A status whose every issue was left out has a line and no row
    /// in the table (D98).
    /// </summary>
    private static void AppendBriefVisits(StringBuilder report, bool leftOutOfTimeInStatus, IReadOnlyList<BriefVisitsRow> rows)
    {
        foreach (var row in rows)
        {
            var threshold = $"{Minutes(row.ThresholdMinutes)} {(row.ThresholdMinutes == 1 ? "minute" : "minutes")}";
            report.Append("- ").Append(Markdown.Text(row.StatusName)).Append(": ");
            if (row.Suppressed == 0 && row.BriefVisitIssueCount is { } issues)
            {
                report.Append(issues.ToString(CultureInfo.InvariantCulture)).Append(issues == 1 ? " issue" : " issues").Append(" had a visit shorter than ").Append(threshold);
            }
            else
            {
                report.Append("issues with a visit shorter than ").Append(threshold).Append(": ").Append(Suppressed);
            }

            if (leftOutOfTimeInStatus)
            {
                report.Append("; those visits are left out of the status's time in status row, and issues that had no other business time in the status and so are not in the row's count: ")
                    .Append(Figure(row.LeftOutSuppressed, row.LeftOutIssueCount?.ToString(CultureInfo.InvariantCulture)));
            }

            report.Append('\n');
        }
    }

    /// <summary>
    /// The footer's cycle time line (GLOSSARY: Brief visit; owner decisions D87 and D100), directly after its
    /// brief visit lines: how many of the sprint's delivered issues have a cycle that starts with a brief
    /// visit, and that they are in the cycle time figures or, with <paramref name="leftOutOfCycleTime"/>, that
    /// they have no cycle time (D85). The number is the same either way. The view has a row for the sprint only when a listed
    /// status has the work role, and without a row there is no line. The view withholds the number as it does
    /// every figure, and a count of nothing with it, so the two read alike.
    /// </summary>
    private static void AppendCyclesThatStartWithBriefVisit(StringBuilder report, bool leftOutOfCycleTime, IReadOnlyList<CycleStartsRow> rows)
    {
        foreach (var row in rows)
        {
            report.Append("- Delivered issues whose cycle starts with a brief visit, which ")
                .Append(leftOutOfCycleTime ? "have no cycle time" : "are in the cycle time figures").Append(": ")
                .Append(Figure(row.Suppressed, row.IssueCount?.ToString(CultureInfo.InvariantCulture))).Append('\n');
        }
    }

    /// <summary>
    /// A threshold in minutes as the footer writes it: the shortest digits that identify the number, as it was
    /// written in the configuration file for a threshold of a few decimals (5, 0.5, 0.69), and always in plain
    /// decimal digits. The round-trip format alone turns to an exponent below 0.0001 (1E-05), which a reader of
    /// a report should not have to decode; its digits are kept and only the notation changes.
    /// </summary>
    private static string Minutes(double minutes)
    {
        var shortest = minutes.ToString("R", CultureInfo.InvariantCulture);
        return shortest.Contains('E', StringComparison.Ordinal)
            ? decimal.Parse(shortest, NumberStyles.Float, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
            : shortest;
    }

    /// <summary>
    /// The head of a table. Every table of the report has this layout: what a row is about in the first
    /// column, and figures in the others, aligned right.
    /// </summary>
    private static void AppendTableHead(StringBuilder report, params ReadOnlySpan<string> columns)
    {
        AppendTableRow(report, columns);
        report.Append("| ---");
        for (var column = 1; column < columns.Length; column++)
        {
            report.Append(" | ---:");
        }

        report.Append(" |\n");
    }

    /// <summary>One row of a table. A cell can hold Jira's text (a status name), so every cell is written as text (<see cref="Markdown.Text"/>).</summary>
    private static void AppendTableRow(StringBuilder report, params ReadOnlySpan<string> cells) =>
        Markdown.AppendRow(report, cells.ToArray().Select(Markdown.Text));

    /// <summary>A number of issues, or <see cref="Suppressed"/> when the view withholds it.</summary>
    private static string Count(long? issues) => issues?.ToString(CultureInfo.InvariantCulture) ?? Suppressed;

    /// <summary>
    /// A point bucket's label: the estimate in full (3, 0.5, 2.001), so two buckets the view keeps apart
    /// never print alike. The round-trip format gives the shortest text that identifies the number.
    /// </summary>
    private static string BucketLabel(double? pointBucket) =>
        pointBucket?.ToString("R", CultureInfo.InvariantCulture) ?? "unestimated";

    /// <summary>A duration in days to one decimal place, e.g. 2.5, or <see cref="Suppressed"/> (see <see cref="Figure"/>). Every figure in days is written by this.</summary>
    private static string Days(long suppressed, double? days) => Figure(suppressed, days?.ToString("F1", CultureInfo.InvariantCulture));

    /// <summary>
    /// A baseline cell. A row the baseline view does not have (no issue of a baseline sprint is in this bucket
    /// or status) carries no flag: nothing is withheld there, so the cell says there is nothing, not "suppressed".
    /// </summary>
    private static string BaselineDays(BaselineColumns baseline, long? suppressed, double? days) =>
        baseline == BaselineColumns.Withheld ? Suppressed : suppressed is { } flag ? Days(flag, days) : NoBaseline;

    /// <summary>What the report does with its baseline columns.</summary>
    private enum BaselineColumns
    {
        /// <summary>No baseline is configured: the tables have no baseline columns.</summary>
        None,

        /// <summary>Each cell is the baseline views' figure, "suppressed" or "none".</summary>
        Shown,

        /// <summary>A baseline sprint has its delivered count withheld: every cell reads "suppressed" (owner decisions D50, D56).</summary>
        Withheld,
    }

    /// <summary>
    /// A cell's text. The figure is printed only when its row says it is not suppressed (0) and carries it;
    /// in every other case the cell reads <see cref="Suppressed"/>, so a figure can be withheld by mistake
    /// but not shown by mistake.
    /// </summary>
    private static string Figure(long suppressed, string? figure) => suppressed == 0 && figure is not null ? figure : Suppressed;

    /// <summary>A stored time as a date in the team's zone, e.g. 2026-01-05.</summary>
    private static string LocalDate(string storedUtc, TimeZoneInfo teamTimeZone)
    {
        var instant = StoredTime.FromStored(storedUtc);
        return TimeZoneInfo.ConvertTime(instant, teamTimeZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private sealed class TimeInStatusRow
    {
        public string StatusName { get; init; } = "";
        /// <summary>1 when the row's status is listed for brief visits and brief visits are left out of time in status; 0 otherwise, and in the one row of a sprint short of a team, which has no status.</summary>
        public long Adjusted { get; init; }
        /// <summary>Null in the one row of a sprint short of a team (five assignees by default): it has no status rows, and that one row is how the report knows to write the "suppressed" line (owner decisions D41, D45).</summary>
        public long? IssueCount { get; init; }
        public double? MedianBusinessDays { get; init; }
        public double? TotalBusinessDays { get; init; }
        public long Suppressed { get; init; } = 1;
        public double? BaselineMedianBusinessDays { get; init; }
        /// <summary>Null when the baseline has no row for the status.</summary>
        public long? BaselineSuppressed { get; init; }
    }

    private sealed class PullRequestsWithoutReviewRow
    {
        public string HostScope { get; init; } = "";
        /// <summary>One of <c>PullRequestCoverageStates</c>: what the counts rest on.</summary>
        public string Coverage { get; init; } = "";
        /// <summary>Each count is null when the view gives none: the row does not rest on a full read and the store holds no such pull request.</summary>
        public long? Waiting { get; init; }
        public long? ClosedWithoutReview { get; init; }
        public long? NoActivity { get; init; }
        public long? AutomationOnly { get; init; }
        /// <summary>Null also for a host that has no Ops group set apart.</summary>
        public long? OpsOnly { get; init; }
        public long? Unclassifiable { get; init; }
        public long? HistoryNotRead { get; init; }
    }

    private sealed class PullRequestTimingRow
    {
        public string Measure { get; init; } = "";
        /// <summary>1 for the view's row of waits counted in no sprint, 0 for the reported sprint's row.</summary>
        public long InNoSprint { get; init; }
        /// <summary>A host of <c>PullRequestHosts</c>, or the combined scope.</summary>
        public string HostScope { get; init; } = "";
        /// <summary>One of <c>PullRequestCoverageStates</c>: what the count rests on.</summary>
        public string Coverage { get; init; } = "";
        /// <summary>Null when the view gives no count: the row's coverage is not a full read and the store holds no wait for it.</summary>
        public long? SampleCount { get; init; }
        /// <summary>Null when no pull request has a wait for the measure in the sprint.</summary>
        public double? MedianElapsedDays { get; init; }
        public double? P85ElapsedDays { get; init; }
        public double? MedianBusinessDays { get; init; }
        public double? P85BusinessDays { get; init; }
    }

    private sealed class DataQualityRow
    {
        public string AsOf { get; init; } = "";
        public string TimeZone { get; init; } = "";
        public long? AnomalyIssueCount { get; init; }
        public long? DeliveredBetweenSprintsCount { get; init; }
        public long Suppressed { get; init; } = 1;
    }

    private sealed class BriefVisitsRow
    {
        public string StatusName { get; init; } = "";
        public double ThresholdMinutes { get; init; }
        public long? BriefVisitIssueCount { get; init; }
        public long Suppressed { get; init; } = 1;
        public long? LeftOutIssueCount { get; init; }
        public long LeftOutSuppressed { get; init; } = 1;
    }

    private sealed class CycleStartsRow
    {
        public long? IssueCount { get; init; }
        public long Suppressed { get; init; } = 1;
    }

    private sealed class DeliveredRow
    {
        public string SprintName { get; init; } = "";
        public string Started { get; init; } = "";
        public string? Ended { get; init; }
        public string? PlannedEnd { get; init; }
        public long? DeliveredCount { get; init; }
        /// <summary>The delivered count's own flag: the team check, and the assignees a figure needs (three by default) among the delivered issues (owner decision D49). The cycle time view withholds its rows under the same check (D54).</summary>
        public long Suppressed { get; init; } = 1;
    }

    private sealed class CycleTimeRow
    {
        public double? PointBucket { get; init; }
        /// <summary>Null in the one row of a sprint whose delivered count is withheld (short of a team, or of the assignees a figure needs among its delivered issues: five and three by default): it has no bucket rows, and that one row is how the report knows to write the "suppressed" line (owner decisions D41, D54).</summary>
        public long? IssueCount { get; init; }
        public double? MedianBusinessDays { get; init; }
        public double? P85BusinessDays { get; init; }
        public double? MedianCalendarDays { get; init; }
        public double? P85CalendarDays { get; init; }
        public long Suppressed { get; init; } = 1;
        public double? BaselineMedianBusinessDays { get; init; }
        public double? BaselineP85BusinessDays { get; init; }
        /// <summary>Null when the baseline has no row for the bucket.</summary>
        public long? BaselineSuppressed { get; init; }
    }
}
