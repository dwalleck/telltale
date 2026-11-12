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

    /// <summary>Renders the report. Dates are shown in <paramref name="teamTimeZone"/>.</summary>
    public static async Task<string> RenderAsync(ITelltaleStore store, string sprintId, TimeZoneInfo teamTimeZone, CancellationToken cancellationToken = default)
    {
        var rows = await store.QueryViewsAsync<DeliveredRow>(
            "SELECT sprint_name, started, planned_end, completed, delivered_count FROM delivered_per_sprint WHERE sprint_id = @sprintId;",
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
        AppendCycleTime(report, await store.QueryViewsAsync<CycleTimeRow>(
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

    private static async Task<IReadOnlyList<TimeInStatusRow>> ReadTimeInStatusAsync(ITelltaleStore store, string sprintId, CancellationToken cancellationToken)
    {
        try
        {
            return await store.QueryViewsAsync<TimeInStatusRow>(
                "SELECT status_name, issue_count, median_business_days FROM time_in_status_per_sprint WHERE sprint_id = @sprintId ORDER BY board_position;",
                new { sprintId },
                cancellationToken);
        }
        catch (DbException exception)
        {
            // The sprint was found a moment ago, so the store is readable: what is missing is this view.
            throw new UserFacingException(
                "The store's derived data has no time in status figures: it was built by an earlier version of this tool. Rebuild the derived data, then report again.",
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
        // A sprint ends when it was completed; one still running shows its planned end.
        var end = sprint.Completed ?? sprint.PlannedEnd;
        var dates = end is null
            ? $"from {LocalDate(sprint.Started, teamTimeZone)}"
            : $"{LocalDate(sprint.Started, teamTimeZone)} to {LocalDate(end, teamTimeZone)}";

        report.Append("# ").Append(sprint.SprintName).Append(" (").Append(dates).Append(")\n\n");
    }

    private static void AppendDeliveredCount(StringBuilder report, DeliveredRow sprint) =>
        report.Append("Delivered issues: ").Append(sprint.DeliveredCount.ToString(CultureInfo.InvariantCulture)).Append('\n');

    /// <summary>The cycle time table: one row per point bucket, smallest estimate first and unestimated issues last.</summary>
    private static void AppendCycleTime(StringBuilder report, IReadOnlyList<CycleTimeRow> buckets)
    {
        report.Append("\n## Cycle time by point bucket\n\n");
        if (buckets.Count == 0)
        {
            report.Append("No issue delivered in this sprint has a cycle time.\n");
            return;
        }

        report.Append("| Point bucket | Issues with a cycle time | Median (business days) | P85 (business days) | Median (calendar days) | P85 (calendar days) |\n");
        report.Append("|---|---|---|---|---|---|\n");

        foreach (var bucket in buckets)
        {
            report.Append("| ").Append(BucketLabel(bucket.PointBucket))
                .Append(" | ").Append(bucket.IssueCount.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(Days(bucket.MedianBusinessDays))
                .Append(" | ").Append(Days(bucket.P85BusinessDays))
                .Append(" | ").Append(Days(bucket.MedianCalendarDays))
                .Append(" | ").Append(Days(bucket.P85CalendarDays))
                .Append(" |\n");
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

        report.Append("| Status | Issues | Median business days |\n| --- | ---: | ---: |\n");
        foreach (var row in rows)
        {
            // A status name is Jira's text: a "|" in it would otherwise end the cell.
            report.Append("| ").Append(row.StatusName.Replace("|", "\\|", StringComparison.Ordinal))
                .Append(" | ").Append(row.IssueCount.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(row.MedianBusinessDays.ToString("0.0", CultureInfo.InvariantCulture))
                .Append(" |\n");
        }
    }

    /// <summary>
    /// A point bucket's label: the estimate in full (3, 0.5, 2.001), so two buckets the view keeps apart
    /// never print alike. The round-trip format gives the shortest text that identifies the number.
    /// </summary>
    private static string BucketLabel(double? pointBucket) =>
        pointBucket?.ToString("R", CultureInfo.InvariantCulture) ?? "unestimated";

    /// <summary>A duration in days to one decimal place, e.g. 2.5.</summary>
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
        public string? PlannedEnd { get; init; }
        public string? Completed { get; init; }
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
