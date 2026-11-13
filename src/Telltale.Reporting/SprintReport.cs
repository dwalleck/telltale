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
    /// get baseline columns and the method note says what the baseline is. <paramref name="numbers"/> are the
    /// minimum group size's numbers the derived data was built under (<see cref="NumbersInForce.Of"/>): the
    /// caller reads them once, and the report states them and decides nothing by them. The same record says
    /// whether a status was listed under <c>briefVisitThresholds</c> (<see cref="NumbersInForce.BriefVisitsChecked"/>):
    /// the method note then explains the footer's brief visit lines, also for a sprint that has none. With no
    /// status listed the report is what it was before brief visits were counted.
    /// </summary>
    public static async Task<string> RenderAsync(ITelltaleStore store, string sprintId, TimeZoneInfo teamTimeZone, IReadOnlyList<string> baselineSprintIds, NumbersInForce numbers, CancellationToken cancellationToken = default)
    {
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
        AppendDeliveredCount(report, sprint);
        AppendCycleTime(report, baseline, await store.QueryViewsAsync<CycleTimeRow>(
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
        AppendTimeInStatus(report, baseline, await ReadTimeInStatusAsync(store, sprintId, cancellationToken));
        var quality = await ReadDataQualityAsync(store, cancellationToken);
        AppendMethodNote(report, quality, numbers, baselineSprintIds, withheldBaselineSprints);
        AppendFooter(report, quality);
        AppendBriefVisits(report, await store.QueryViewsAsync<BriefVisitsRow>(
            "SELECT status_name, threshold_minutes, brief_visit_issue_count, suppressed FROM brief_visits_per_sprint WHERE sprint_id = @sprintId ORDER BY board_position;",
            new { sprintId },
            cancellationToken));
        return report.ToString();
    }

    private static Task<IReadOnlyList<TimeInStatusRow>> ReadTimeInStatusAsync(ITelltaleStore store, string sprintId, CancellationToken cancellationToken) =>
        store.QueryViewsAsync<TimeInStatusRow>(
            """
            SELECT v.status_name, v.issue_count, v.median_business_days, v.total_business_days, v.suppressed,
                   b.median_business_days AS baseline_median_business_days, b.suppressed AS baseline_suppressed
            FROM time_in_status_per_sprint v
            LEFT JOIN baseline_time_in_status b ON b.status_id = v.status_id
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
    public static async Task<string> WriteAsync(ITelltaleStore store, string sprintId, TimeZoneInfo teamTimeZone, IReadOnlyList<string> baselineSprintIds, NumbersInForce numbers, string directory, CancellationToken cancellationToken = default)
    {
        var text = await RenderAsync(store, sprintId, teamTimeZone, baselineSprintIds, numbers, cancellationToken);

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

    /// <summary>"below the default of five", "the default", or "above the default of five".</summary>
    private static string AgainstTheDefault(int number, int byDefault) =>
        number == byDefault ? "the default" : $"{(number < byDefault ? "below" : "above")} the default of {NumbersInForce.Number(byDefault)}";

    private static void AppendDeliveredCount(StringBuilder report, DeliveredRow sprint) =>
        report.Append("Delivered issues: ").Append(Figure(sprint.Suppressed, sprint.DeliveredCount?.ToString(CultureInfo.InvariantCulture))).Append('\n');

    /// <summary>The cycle time table: one row per point bucket, smallest estimate first and unestimated issues last.</summary>
    private static void AppendCycleTime(StringBuilder report, BaselineColumns baseline, IReadOnlyList<CycleTimeRow> buckets)
    {
        report.Append("\n## Cycle time by point bucket\n\n");
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

    /// <summary>One row per status issues spent time in during the sprint, in board order. A status nobody was in has no row.</summary>
    private static void AppendTimeInStatus(StringBuilder report, BaselineColumns baseline, IReadOnlyList<TimeInStatusRow> rows)
    {
        report.Append("\n## Time in status\n\n");
        if (rows is [{ IssueCount: null }])
        {
            AppendSuppressedTable(report);
            return;
        }

        if (rows.Count == 0)
        {
            report.Append("No issue of this sprint spent time in a status that is not done.\n");
            return;
        }

        string[] baselineHead = baseline == BaselineColumns.None ? [] : ["Baseline median (business days)"];
        AppendTableHead(report, ["Status", "Issues", "Median (business days)", "Total (business days)", .. baselineHead]);
        foreach (var row in rows)
        {
            string[] baselineCells = baseline == BaselineColumns.None ? [] : [BaselineDays(baseline, row.BaselineSuppressed, row.BaselineMedianBusinessDays)];
            AppendTableRow(report, [row.StatusName, Count(row.IssueCount), Days(row.Suppressed, row.MedianBusinessDays), Days(row.Suppressed, row.TotalBusinessDays), .. baselineCells]);
        }
    }

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
    private static void AppendMethodNote(StringBuilder report, DataQualityRow quality, NumbersInForce numbers, IReadOnlyList<string> baselineSprintIds, IReadOnlyList<string> withheldBaselineSprints)
    {
        report.Append("\n## Method\n\n")
            .Append("- Durations are in business days: days that are neither a weekend nor a company holiday, each counted in full (24 hours) in the team's time zone (").Append(quality.TimeZone).Append("). Calendar days are shown alongside: elapsed time divided by 24 hours.\n")
            .Append("- Cycle time runs from an issue's first entry into a work status to its last entry into a done status, for delivered issues only. It belongs to the sprint in which the issue was delivered.\n")
            .Append("- The median is the middle value, or the mean of the two middle values. The P85 is the smallest value with at least 85% of the values at or below it.\n")
            .Append("- Time in status includes delivered and unfinished issues in statuses before done, clipped to the sprint window and each issue's membership intervals. An issue counts toward a status only if it spent more than zero business days there. Total business days sum that time across all contributing issues, including repeated visits; this is accumulated issue time, not effort or the sprint's elapsed duration. Totals and medians use the same privacy rules.\n");
        AppendMinimumGroupSize(report, numbers);

        // Said only when there is a baseline: without one the report has no baseline column to explain.
        if (baselineSprintIds.Count > 0)
        {
            report.Append("- The baseline columns pool every issue of the baseline sprints, a list fixed in the configuration (sprints ").AppendJoin(", ", baselineSprintIds)
                .Append("), each a completed sprint. ")
                .Append(WhenABaselineFigureIsNotShown(numbers)).Append('\n');
        }

        if (withheldBaselineSprints.Count > 0)
        {
            report.Append("- ").Append(BaselineSprintRules.WithheldInTheReport(withheldBaselineSprints, numbers)).Append('\n');
        }

        report.Append("- An interval that is still open (an issue still in a status, a sprint not yet completed) ends at the as-of time below.\n")
            .Append("- Work that has since been removed from Jira stays in the figures until the next backfill.\n");
        if (numbers.BriefVisitsChecked)
        {
            AppendBriefVisitsExplained(report, numbers);
        }
    }

    /// <summary>
    /// The method note's paragraph on brief visits (GLOSSARY: Brief visit; owner decisions D60 to D62), written
    /// only when a status is listed. It says what a brief visit is, that brief visits are counted and no figure
    /// was changed because of one, what a count reads in place of a number, and that the rule was added after
    /// the first run on real data. What a count reads follows the numbers in force (D67), as the view does
    /// (<c>brief_visits_per_sprint</c>): while a figure needs assignees behind it, a count of nothing has nobody
    /// behind it and is withheld as a count with too few is; with that number at zero no count is withheld and
    /// a count of nothing is printed as 0. The lines come from the time in status table's rows, so a sprint
    /// short of a team has none.
    /// </summary>
    private static void AppendBriefVisitsExplained(StringBuilder report, NumbersInForce numbers)
    {
        report.Append("- A brief visit is one unbroken stay in a status that ended and was shorter than the status's threshold.")
            .Append(" It is measured in clock time over the issue's whole history: weekends and holidays count, and the stay is not clipped to the sprint.")
            .Append(" For each status the configuration file lists (briefVisitThresholds), the footer gives how many of the issues in the status's time in status row had a brief visit during the time the row counts.")
            .Append(" Brief visits are counted and nothing more: no duration was excluded or corrected because of one, and every other figure is what it would be without the count.")
            .Append(" A short median in the time in status table does not show how many issues had a brief visit.");
        if (numbers.FigureCheck)
        {
            report.Append(" A count reads \"suppressed\" when its row in the time in status table reads \"suppressed\" for its figures, or when fewer than ")
                .Append(numbers.AssigneesBehindAFigure(distinct: true)).Append(' ').Append(numbers.BehindAFigureAre).Append(" among the issues counted.")
                .Append(" A count of nothing has nobody behind it, so a zero and a withheld count both read \"suppressed\".");
        }
        else
        {
            report.Append(numbers.TeamCheck ? " No count is withheld for the number of assignees behind it" : " No count is withheld for group size")
                .Append(", and a count of nothing reads 0.");
        }

        report.Append(" A listed status with no row in the time in status table has no line")
            .Append(numbers.TeamCheck ? $", and a sprint whose team is smaller than {numbers.TeamInWords} has none at all." : ".")
            .Append(" This rule was added on 2026-11-12, after the first run on real data: the thresholds were chosen with knowledge of results, and the counts describe data quality and are not a success criterion.\n");
    }

    /// <summary>
    /// The method note's paragraphs on the minimum group size and on who counts as an assignee, with the numbers
    /// in force (owner decision D67). Each sentence is about one of the two checks and is written only while
    /// that check applies: a number of zero turns its check off (D65). With both off, the two paragraphs give
    /// way to one that says so. With a status listed for brief visits the footer can have brief visit lines
    /// after its two counts of the whole store, withheld by their own rule (<see cref="AppendBriefVisitsExplained"/>),
    /// so the sentence on those two says which they are, whether or not this sprint has such a line.
    /// </summary>
    private static void AppendMinimumGroupSize(StringBuilder report, NumbersInForce numbers)
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
                .Append(behindAFigure).Append(' ').Append(are).Append(" among the delivered issues, and the cycle time table then reads \"suppressed\" as well, with no rows, because the number of its rows and their counts (the delivered issues that have a cycle time) would each give a lower bound of the withheld count. The time in status table of such a sprint keeps its counts: they count every issue that spent time in a status, delivered or not, so the count of a late status can be close to the withheld delivered count.");
        }
        else
        {
            // Of the sprint itself: a baseline figure beside it can still be withheld, for a baseline sprint's team.
            report.Append(" With a team of ").Append(team).Append(" or more, every figure of the sprint itself is shown, whatever the number of assignees behind it.");
        }

        report.Append(numbers.BriefVisitsChecked ? " The footer's two counts of the whole store" : " The footer's two counts").Append(" read \"suppressed\" when ");
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
    /// board order. A line names the status and states its threshold. The view withholds a count as it does
    /// every figure, and a count of nothing with it, so the two read alike.
    /// </summary>
    private static void AppendBriefVisits(StringBuilder report, IReadOnlyList<BriefVisitsRow> rows)
    {
        foreach (var row in rows)
        {
            var threshold = $"{Minutes(row.ThresholdMinutes)} {(row.ThresholdMinutes == 1 ? "minute" : "minutes")}";
            report.Append("- ").Append(Markdown.Text(row.StatusName)).Append(": ");
            if (row.Suppressed == 0 && row.BriefVisitIssueCount is { } issues)
            {
                report.Append(issues.ToString(CultureInfo.InvariantCulture)).Append(issues == 1 ? " issue" : " issues").Append(" had a visit shorter than ").Append(threshold).Append('\n');
            }
            else
            {
                report.Append("issues with a visit shorter than ").Append(threshold).Append(": ").Append(Suppressed).Append('\n');
            }
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
        /// <summary>Null in the one row of a sprint short of a team (five assignees by default): it has no status rows, and that one row is how the report knows to write the "suppressed" line (owner decisions D41, D45).</summary>
        public long? IssueCount { get; init; }
        public double? MedianBusinessDays { get; init; }
        public double? TotalBusinessDays { get; init; }
        public long Suppressed { get; init; } = 1;
        public double? BaselineMedianBusinessDays { get; init; }
        /// <summary>Null when the baseline has no row for the status.</summary>
        public long? BaselineSuppressed { get; init; }
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
