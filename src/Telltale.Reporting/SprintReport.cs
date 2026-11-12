using System.Data.Common;
using System.Globalization;
using System.Text;
using Telltale.Core;
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
    /// The IDs of the sprints the store's baseline figures are pooled over: the configured list as it was when
    /// the derived data was built. Empty when there is no baseline.
    /// </summary>
    public static Task<IReadOnlyList<string>> BaselineSprintIdsAsync(ITelltaleStore store, CancellationToken cancellationToken = default) =>
        store.QueryViewsAsync<string>("SELECT sprint_id FROM baseline_sprints ORDER BY sprint_id;", cancellationToken: cancellationToken);

    /// <summary>
    /// Renders the report. Dates are shown in <paramref name="teamTimeZone"/>. With <paramref name="withBaseline"/>
    /// both tables get baseline columns: pass whether <see cref="BaselineSprintIdsAsync"/> lists any sprint.
    /// </summary>
    public static async Task<string> RenderAsync(ITelltaleStore store, string sprintId, TimeZoneInfo teamTimeZone, bool withBaseline, CancellationToken cancellationToken = default)
    {
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
        AppendDeliveredCount(report, sprint);
        AppendCycleTime(report, withBaseline, await store.QueryViewsAsync<CycleTimeRow>(
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
        AppendTimeInStatus(report, withBaseline, await ReadTimeInStatusAsync(store, sprintId, cancellationToken));
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

    /// <summary>Renders the report and writes it into <paramref name="directory"/>. Returns the file's path.</summary>
    public static async Task<string> WriteAsync(ITelltaleStore store, string sprintId, TimeZoneInfo teamTimeZone, bool withBaseline, string directory, CancellationToken cancellationToken = default)
    {
        var text = await RenderAsync(store, sprintId, teamTimeZone, withBaseline, cancellationToken);

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

        report.Append("# ").Append(sprint.SprintName).Append(" (").Append(dates).Append(")\n\n");
    }

    private static void AppendDeliveredCount(StringBuilder report, DeliveredRow sprint) =>
        report.Append("Delivered issues: ").Append(Figure(sprint.Suppressed, sprint.DeliveredCount?.ToString(CultureInfo.InvariantCulture))).Append('\n');

    /// <summary>The cycle time table: one row per point bucket, smallest estimate first and unestimated issues last.</summary>
    private static void AppendCycleTime(StringBuilder report, bool withBaseline, IReadOnlyList<CycleTimeRow> buckets)
    {
        report.Append("\n## Cycle time by point bucket\n\n");
        if (buckets.Count == 0)
        {
            report.Append("No issue delivered in this sprint has a cycle time.\n");
            return;
        }

        string[] baselineHead = withBaseline ? ["Baseline median (business days)", "Baseline P85 (business days)"] : [];
        AppendTableHead(report, ["Point bucket", "Issues with a cycle time", "Median (business days)", "P85 (business days)", "Median (calendar days)", "P85 (calendar days)", .. baselineHead]);
        foreach (var bucket in buckets)
        {
            string[] baseline = withBaseline
                ? [BaselineDays(bucket.BaselineSuppressed, bucket.BaselineMedianBusinessDays), BaselineDays(bucket.BaselineSuppressed, bucket.BaselineP85BusinessDays)]
                : [];
            AppendTableRow(report, [BucketLabel(bucket.PointBucket), Count(bucket.IssueCount), Days(bucket.Suppressed, bucket.MedianBusinessDays), Days(bucket.Suppressed, bucket.P85BusinessDays), Days(bucket.Suppressed, bucket.MedianCalendarDays), Days(bucket.Suppressed, bucket.P85CalendarDays), .. baseline]);
        }
    }

    /// <summary>One row per status issues spent time in during the sprint, in board order. A status nobody was in has no row.</summary>
    private static void AppendTimeInStatus(StringBuilder report, bool withBaseline, IReadOnlyList<TimeInStatusRow> rows)
    {
        report.Append("\n## Time in status\n\n");
        if (rows.Count == 0)
        {
            report.Append("No issue of this sprint spent time in a status that is not done.\n");
            return;
        }

        string[] baselineHead = withBaseline ? ["Baseline median (business days)"] : [];
        AppendTableHead(report, ["Status", "Issues", "Median (business days)", .. baselineHead]);
        foreach (var row in rows)
        {
            string[] baseline = withBaseline ? [BaselineDays(row.BaselineSuppressed, row.BaselineMedianBusinessDays)] : [];
            AppendTableRow(report, [row.StatusName, Count(row.IssueCount), Days(row.Suppressed, row.MedianBusinessDays), .. baseline]);
        }
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

    private static void AppendTableRow(StringBuilder report, params ReadOnlySpan<string> cells)
    {
        foreach (var cell in cells)
        {
            // A cell can hold Jira's text (a status name): a "|" in it would otherwise end the cell.
            report.Append("| ").Append(cell.Replace("|", "\\|", StringComparison.Ordinal)).Append(' ');
        }

        report.Append("|\n");
    }

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
    private static string BaselineDays(long? suppressed, double? days) => suppressed is { } flag ? Days(flag, days) : NoBaseline;

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

    private sealed class DeliveredRow
    {
        public string SprintName { get; init; } = "";
        public string Started { get; init; } = "";
        public string? Ended { get; init; }
        public string? PlannedEnd { get; init; }
        public long? DeliveredCount { get; init; }
        public long Suppressed { get; init; } = 1;
    }

    private sealed class CycleTimeRow
    {
        public double? PointBucket { get; init; }
        /// <summary>Null when the sprint is short of five assignees: its counts are withheld with its figures (owner decision D41).</summary>
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
