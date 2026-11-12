using System.Globalization;

namespace Telltale.Core.Derive;

/// <summary>
/// The before-range rule, written once. The store holds only issues updated since the start date it was last
/// filled from, so issues delivered in a sprint that started before that date and not touched since are
/// missing, and any figure of such a sprint would be too low without a sign. Whoever shows a sprint's figures
/// (a report of it, a baseline pooled from it) asks here and says what follows for itself.
/// </summary>
public static class FetchedRange
{
    /// <summary>
    /// The IDs, in order, of the sprints that started before <paramref name="completeFrom"/>, the moment from
    /// which the store holds every issue. None when the store names no such moment (null).
    /// </summary>
    public static IReadOnlyList<string> StartedBefore(IEnumerable<(string SprintId, DateTimeOffset Start)> starts, DateTimeOffset? completeFrom) =>
        [.. starts.Where(sprint => sprint.Start < completeFrom).Select(sprint => sprint.SprintId).Order(StringComparer.Ordinal)];

    /// <summary>Stops when <paramref name="sprintId"/>, the sprint a report is asked for, started before <paramref name="completeFrom"/>.</summary>
    public static void RequireReportable(string sprintId, IEnumerable<(string SprintId, DateTimeOffset Start)> starts, DateTimeOffset? completeFrom)
    {
        if (StartedBefore(starts.Where(sprint => sprint.SprintId == sprintId), completeFrom).Count > 0)
        {
            throw new UserFacingException(
                $"Sprint {sprintId} started before {Date(completeFrom)}, the jira.backfillStart the store was last filled from, "
                + "so issues delivered in it can be missing and its figures would be too low. "
                + "No report was written. To report on it, set jira.backfillStart to a date before the sprint started and run backfill.");
        }
    }

    /// <summary>The date of <paramref name="completeFrom"/> as messages give it.</summary>
    public static string? Date(DateTimeOffset? completeFrom) => completeFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
