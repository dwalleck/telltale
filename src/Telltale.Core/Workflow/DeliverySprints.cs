using Telltale.Core.Intervals;

namespace Telltale.Core.Workflow;

/// <summary>
/// Finds the sprint a delivery belongs to: the one whose sprint window contains the delivery moment.
/// The windows are resolved and ordered once, then asked about every delivered issue.
/// </summary>
public sealed class DeliverySprints
{
    private readonly (string SprintId, Interval Window)[] _windows;

    public DeliverySprints(IEnumerable<SprintWindow> sprints, DateTimeOffset asOf)
    {
        _windows = [.. sprints.OrderBy(sprint => sprint.Start).Select(sprint => (sprint.SprintId, sprint.Resolve(asOf)))];
    }

    /// <summary>
    /// The sprint whose window contains <paramref name="deliveredAt"/>, or null when it falls between
    /// sprints. If windows overlap, the sprint that started first wins.
    /// </summary>
    public string? For(DateTimeOffset deliveredAt)
    {
        foreach (var (sprintId, window) in _windows)
        {
            if (Contains(window, deliveredAt))
            {
                return sprintId;
            }
        }

        return null;
    }

    private static bool Contains(Interval window, DateTimeOffset moment) => window.Start <= moment && moment < window.End;
}
