using System.Data.Common;
using System.Globalization;
using System.Text;
using Telltale.Core;
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
    /// get baseline columns and the method note says what the baseline is.
    /// </summary>
    public static async Task<string> RenderAsync(ITelltaleStore store, string sprintId, TimeZoneInfo teamTimeZone, IReadOnlyList<string> baselineSprintIds, CancellationToken cancellationToken = default)
    {
        // Owner decision D50: while a baseline sprint has a team smaller than five, every baseline figure is
        // withheld. The baseline views have no rows then, so the cells are written from this list.
        var smallBaselineSprints = baselineSprintIds.Count == 0
            ? []
            : await store.QueryViewsAsync<string>("SELECT sprint_id FROM baseline_sprint WHERE suppressed <> 0 ORDER BY sprint_id;", cancellationToken: cancellationToken);
        var baseline = baselineSprintIds.Count == 0 ? BaselineColumns.None : smallBaselineSprints.Count > 0 ? BaselineColumns.Withheld : BaselineColumns.Shown;
        var rows = await store.QueryViewsAsync<DeliveredRow>(
            "SELECT sprint_name, started, ended, planned_end, delivered_count, suppressed, team_suppressed FROM delivered_per_sprint WHERE sprint_id = @sprintId;",
            new { sprintId },
            cancellationToken);

        if (rows is not [var sprint])
        {
            throw new UserFacingException($"Sprint {sprintId} is not in the store, or has not started. Check the ID against the board's sprints.");
        }

        // Each section is one block of the report, in order. A new section is a new method and a line here.
        var report = new StringBuilder();
        AppendTitle(report, sprint, teamTimeZone);
        AppendDeliveredCount(report, sprint);
        AppendCycleTime(report, sprint, baseline, await store.QueryViewsAsync<CycleTimeRow>(
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
        AppendTimeInStatus(report, sprint, baseline, await ReadTimeInStatusAsync(store, sprintId, cancellationToken));
        var quality = await ReadDataQualityAsync(store, cancellationToken);
        AppendMethodNote(report, quality, baselineSprintIds, smallBaselineSprints);
        AppendFooter(report, quality);
        return report.ToString();
    }

    private static Task<IReadOnlyList<TimeInStatusRow>> ReadTimeInStatusAsync(ITelltaleStore store, string sprintId, CancellationToken cancellationToken) =>
        store.QueryViewsAsync<TimeInStatusRow>(
            """
            SELECT v.status_name, v.issue_count, v.median_business_days, v.suppressed,
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
    public static async Task<string> WriteAsync(ITelltaleStore store, string sprintId, TimeZoneInfo teamTimeZone, IReadOnlyList<string> baselineSprintIds, string directory, CancellationToken cancellationToken = default)
    {
        var text = await RenderAsync(store, sprintId, teamTimeZone, baselineSprintIds, cancellationToken);

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

    private static void AppendDeliveredCount(StringBuilder report, DeliveredRow sprint) =>
        report.Append("Delivered issues: ").Append(Figure(sprint.Suppressed, sprint.DeliveredCount?.ToString(CultureInfo.InvariantCulture))).Append('\n');

    /// <summary>The cycle time table: one row per point bucket, smallest estimate first and unestimated issues last.</summary>
    private static void AppendCycleTime(StringBuilder report, DeliveredRow sprint, BaselineColumns baseline, IReadOnlyList<CycleTimeRow> buckets)
    {
        report.Append("\n## Cycle time by point bucket\n\n");
        if (AppendSuppressedTable(report, sprint.Suppressed))
        {
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
    private static void AppendTimeInStatus(StringBuilder report, DeliveredRow sprint, BaselineColumns baseline, IReadOnlyList<TimeInStatusRow> rows)
    {
        report.Append("\n## Time in status\n\n");
        if (AppendSuppressedTable(report, sprint.TeamSuppressed))
        {
            return;
        }

        if (rows.Count == 0)
        {
            report.Append("No issue of this sprint spent time in a status that is not done.\n");
            return;
        }

        string[] baselineHead = baseline == BaselineColumns.None ? [] : ["Baseline median (business days)"];
        AppendTableHead(report, ["Status", "Issues", "Median (business days)", .. baselineHead]);
        foreach (var row in rows)
        {
            string[] baselineCells = baseline == BaselineColumns.None ? [] : [BaselineDays(baseline, row.BaselineSuppressed, row.BaselineMedianBusinessDays)];
            AppendTableRow(report, [row.StatusName, Count(row.IssueCount), Days(row.Suppressed, row.MedianBusinessDays), .. baselineCells]);
        }
    }

    /// <summary>
    /// For a table the view withholds as a whole, writes the one line that stands for it and returns true: no
    /// row, and no sentence about an empty table either, because which buckets or statuses have rows says
    /// what the sprint's few people worked on (owner decision D45). <paramref name="suppressed"/> is the flag
    /// of the check the table follows: both tables follow the team check, and the cycle time table also
    /// follows the check of the delivered count, whose flag covers both (owner decision D54).
    /// </summary>
    private static bool AppendSuppressedTable(StringBuilder report, long suppressed)
    {
        if (suppressed == 0)
        {
            return false;
        }

        report.Append(Suppressed).Append('\n');
        return true;
    }

    /// <summary>
    /// What the figures mean, with each rule as the glossary and the owner's decisions state it. The suppression
    /// rule is ADR-0003's for results grouped by issue attributes. The time zone is the one derive stored.
    /// </summary>
    private static void AppendMethodNote(StringBuilder report, DataQualityRow quality, IReadOnlyList<string> baselineSprintIds, IReadOnlyList<string> smallBaselineSprints)
    {
        report.Append("\n## Method\n\n")
            .Append("- Durations are in business days: days that are neither a weekend nor a company holiday, each counted in full (24 hours) in the team's time zone (").Append(quality.TimeZone).Append("). Calendar days are shown alongside: elapsed time divided by 24 hours.\n")
            .Append("- Cycle time runs from an issue's first entry into a work status to its last entry into a done status, for delivered issues only. It belongs to the sprint in which the issue was delivered.\n")
            .Append("- Figures are medians and P85s. The median is the middle value, or the mean of the two middle values. The P85 is the smallest value with at least 85% of the values at or below it.\n")
            .Append("- Time in status covers the statuses before done. An issue counts toward a status only if it spent more than zero business days there.\n")
            .Append("- A figure is shown only where it cannot show one person's work. The team of a sprint is the distinct assignees of every issue that was a member of the sprint or was delivered in it. When the team is smaller than five, the delivered count and both tables read \"suppressed\", with no rows. With a team of five or more, a row with fewer than three distinct assignees behind it keeps its issue count and reads \"suppressed\" for its figures, and the delivered count reads \"suppressed\" when fewer than three distinct assignees are among the delivered issues, and the cycle time table then reads \"suppressed\" as well, with no rows, because its rows and their counts would give the count back. The time in status table of such a sprint keeps its counts: they count every issue that spent time in a status, delivered or not, so the count of a late status can be close to the withheld delivered count. The footer's two counts read \"suppressed\" when the issues in the whole store have fewer than five distinct assignees, or fewer than three are among the issues counted.\n")
            .Append("- An assignee is a Jira account as of the last sync, so a bot or a person's second account counts as a person. An unassigned issue counts toward nobody.\n");

        // Said only when there is a baseline: without one the report has no baseline column to explain.
        if (baselineSprintIds.Count > 0)
        {
            report.Append("- The baseline columns pool every issue of the baseline sprints, a list fixed in the configuration (sprints ").AppendJoin(", ", baselineSprintIds)
                .Append("), each a completed sprint. A baseline figure reads \"suppressed\" when any baseline sprint has that row suppressed in its own report, so that a pooled figure cannot give back a withheld one. It reads \"none\" when no issue of the baseline sprints is in the row. When a baseline sprint has a team smaller than five, every baseline figure reads \"suppressed\".\n");
        }

        if (smallBaselineSprints.Count > 0)
        {
            report.Append("- ").Append(BaselineSprintRules.SprintsNamed(smallBaselineSprints)).Append(smallBaselineSprints.Count == 1 ? " has" : " each have")
                .Append(" a team smaller than five, so every baseline figure in this report reads \"suppressed\".\n");
        }

        report.Append("- An interval that is still open (an issue still in a status, a sprint not yet completed) ends at the as-of time below.\n")
            .Append("- Work that has since been removed from Jira stays in the figures until the next backfill.\n");
    }

    /// <summary>
    /// The data-quality footer. Its two counts cover the whole store, not the sprint: an issue delivered
    /// between sprints belongs to no sprint. They are counts only: no issue is named. The view withholds
    /// them when the whole store has fewer than five distinct assignees, or fewer than three are among the
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

        /// <summary>A baseline sprint has a team smaller than five: every cell reads "suppressed" (owner decision D50).</summary>
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
        /// <summary>Null when the sprint is short of five assignees: its counts are withheld with its figures (owner decision D41).</summary>
        public long? IssueCount { get; init; }
        public double? MedianBusinessDays { get; init; }
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

    private sealed class DeliveredRow
    {
        public string SprintName { get; init; } = "";
        public string Started { get; init; } = "";
        public string? Ended { get; init; }
        public string? PlannedEnd { get; init; }
        public long? DeliveredCount { get; init; }
        /// <summary>The delivered count's own flag: the team check, and three assignees among the delivered issues (owner decision D49). The cycle time table follows it too (D54).</summary>
        public long Suppressed { get; init; } = 1;
        /// <summary>The team check alone: 1 for a sprint whose tables both show no rows. The time in status table follows it.</summary>
        public long TeamSuppressed { get; init; } = 1;
    }

    private sealed class CycleTimeRow
    {
        public double? PointBucket { get; init; }
        /// <summary>Null in the one row of a sprint whose delivered count is withheld (short of five assignees, or of three among its delivered issues): it has no bucket rows, so the report never prints this row (owner decisions D41, D54).</summary>
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
