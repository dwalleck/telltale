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
    /// <summary>The report's file name for a sprint: reports are named by sprint ID.</summary>
    public static string FileName(string sprintId) => $"sprint-{sprintId}.md";

    /// <summary>
    /// The ID of the sprint that completed last, or null when none has. When a sprint ended is its
    /// sprint window's end, which derive decides.
    /// </summary>
    public static async Task<string?> MostRecentlyCompletedSprintAsync(ITelltaleStore store, CancellationToken cancellationToken = default)
    {
        var sprints = await QueryAsync<string>(
            store,
            "SELECT sprint_id FROM delivered_per_sprint WHERE state = @closed ORDER BY ended DESC, started DESC, sprint_id DESC LIMIT 1;",
            new { closed = SprintStates.Closed },
            cancellationToken);

        return sprints is [var sprintId] ? sprintId : null;
    }

    /// <summary>Renders the report. Dates are shown in <paramref name="teamTimeZone"/>.</summary>
    public static async Task<string> RenderAsync(ITelltaleStore store, string sprintId, TimeZoneInfo teamTimeZone, CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync<DeliveredRow>(
            store,
            "SELECT sprint_name, started, ended, planned_end, delivered_count FROM delivered_per_sprint WHERE sprint_id = @sprintId;",
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
        AppendCycleTime(report, await QueryAsync<CycleTimeRow>(
            store,
            """
            SELECT point_bucket, issue_count, median_business_days, p85_business_days, median_calendar_days, p85_calendar_days
            FROM cycle_time_per_sprint_and_bucket
            WHERE sprint_id = @sprintId
            ORDER BY point_bucket IS NULL, point_bucket;
            """,
            new { sprintId },
            cancellationToken));
        AppendTimeInStatus(report, await ReadTimeInStatusAsync(store, sprintId, cancellationToken));
        return report.ToString();
    }

    private static Task<IReadOnlyList<TimeInStatusRow>> ReadTimeInStatusAsync(ITelltaleStore store, string sprintId, CancellationToken cancellationToken) =>
        QueryAsync<TimeInStatusRow>(
            store,
            "SELECT status_name, issue_count, median_business_days FROM time_in_status_per_sprint WHERE sprint_id = @sprintId ORDER BY board_position;",
            new { sprintId },
            cancellationToken);

    /// <summary>
    /// Every query of the report goes through here. The report reads metric views only. When the store says
    /// a view or column is not there, derive has not run on it, or a version without that view built the
    /// derived data. The report command checks the store before it gets here; another caller gets the same
    /// plain message. Any other failure of the database (locked, damaged, unopenable) is not cured by derive
    /// and is left as the database reports it.
    /// </summary>
    private static async Task<IReadOnlyList<T>> QueryAsync<T>(ITelltaleStore store, string sql, object parameters, CancellationToken cancellationToken)
    {
        try
        {
            return await store.QueryViewsAsync<T>(sql, parameters, cancellationToken);
        }
        catch (ViewsNotBuiltException exception)
        {
            throw new UserFacingException(
                "The store's derived data cannot be read: it has not been built yet, or another version of this tool built it. Run derive, then report again.",
                exception);
        }
    }

    /// <summary>Renders the report and writes it into <paramref name="directory"/>. Returns the file's path.</summary>
    public static async Task<string> WriteAsync(ITelltaleStore store, string sprintId, TimeZoneInfo teamTimeZone, string directory, CancellationToken cancellationToken = default)
    {
        var text = await RenderAsync(store, sprintId, teamTimeZone, cancellationToken);

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
        report.Append("Delivered issues: ").Append(Count(sprint.DeliveredCount)).Append('\n');

    /// <summary>The cycle time table: one row per point bucket, smallest estimate first and unestimated issues last.</summary>
    private static void AppendCycleTime(StringBuilder report, IReadOnlyList<CycleTimeRow> buckets)
    {
        report.Append("\n## Cycle time by point bucket\n\n");
        if (buckets.Count == 0)
        {
            report.Append("No issue delivered in this sprint has a cycle time.\n");
            return;
        }

        AppendTableHead(report, "Point bucket", "Issues with a cycle time", "Median (business days)", "P85 (business days)", "Median (calendar days)", "P85 (calendar days)");
        foreach (var bucket in buckets)
        {
            AppendTableRow(report, BucketLabel(bucket.PointBucket), Count(bucket.IssueCount), Days(bucket.MedianBusinessDays), Days(bucket.P85BusinessDays), Days(bucket.MedianCalendarDays), Days(bucket.P85CalendarDays));
        }
    }

    /// <summary>One row per status issues spent time in during the sprint, in board order. A status nobody was in has no row.</summary>
    private static void AppendTimeInStatus(StringBuilder report, IReadOnlyList<TimeInStatusRow> rows)
    {
        report.Append("\n## Time in status\n\n");
        if (rows.Count == 0)
        {
            report.Append("No issue of this sprint spent time in a status that is not done.\n");
            return;
        }

        AppendTableHead(report, "Status", "Issues", "Median (business days)");
        foreach (var row in rows)
        {
            AppendTableRow(report, row.StatusName, Count(row.IssueCount), Days(row.MedianBusinessDays));
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

    /// <summary>A number of issues.</summary>
    private static string Count(long issues) => issues.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A point bucket's label: the estimate in full (3, 0.5, 2.001), so two buckets the view keeps apart
    /// never print alike. The round-trip format gives the shortest text that identifies the number.
    /// </summary>
    private static string BucketLabel(double? pointBucket) =>
        pointBucket?.ToString("R", CultureInfo.InvariantCulture) ?? "unestimated";

    /// <summary>A duration in days to one decimal place, e.g. 2.5. Every figure in days is written by this.</summary>
    private static string Days(double days) => days.ToString("F1", CultureInfo.InvariantCulture);

    /// <summary>A stored time as a date in the team's zone, e.g. 2026-01-05.</summary>
    private static string LocalDate(string storedUtc, TimeZoneInfo teamTimeZone)
    {
        var instant = StoredTime.FromStored(storedUtc);
        return TimeZoneInfo.ConvertTime(instant, teamTimeZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private sealed class TimeInStatusRow
    {
        public string StatusName { get; init; } = "";
        public long IssueCount { get; init; }
        public double MedianBusinessDays { get; init; }
    }

    private sealed class DeliveredRow
    {
        public string SprintName { get; init; } = "";
        public string Started { get; init; } = "";
        public string? Ended { get; init; }
        public string? PlannedEnd { get; init; }
        public long DeliveredCount { get; init; }
    }

    private sealed class CycleTimeRow
    {
        public double? PointBucket { get; init; }
        public long IssueCount { get; init; }
        public double MedianBusinessDays { get; init; }
        public double P85BusinessDays { get; init; }
        public double MedianCalendarDays { get; init; }
        public double P85CalendarDays { get; init; }
    }
}
