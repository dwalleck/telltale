using Telltale.Core.Intervals;

namespace Telltale.Core.Workflow;

public static class DeliverySprint
{
    /// <summary>
    /// The sprint a delivery belongs to: the one whose sprint window contains the delivery moment.
    /// Null when the moment falls between sprints. If windows overlap, the sprint that started first wins.
    /// </summary>
    public static string? For(DateTimeOffset deliveredAt, IEnumerable<SprintWindow> sprints, DateTimeOffset asOf) =>
        sprints
            .OrderBy(sprint => sprint.Start)
            .FirstOrDefault(sprint => Contains(sprint.Resolve(asOf), deliveredAt))
            ?.SprintId;

    private static bool Contains(Interval window, DateTimeOffset moment) => window.Start <= moment && moment < window.End;
}
