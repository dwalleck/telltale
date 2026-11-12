using Telltale.Core.Intervals;
using Telltale.Core.Storage;

namespace Telltale.Core.Derive;

/// <summary>Which sprints have a sprint window, and when it ends (GLOSSARY: Sprint window). Decided here and nowhere else.</summary>
public static class SprintWindows
{
    /// <summary>
    /// The sprint's window, or null when it has none. Only a sprint that has started has one: a future sprint
    /// has none even when it carries planned dates, and neither has a sprint Jira gives no start date. The
    /// window ends at the sprint's completion; for a closed sprint Jira gives no completion time, at its
    /// planned end (owner decision D7); and not at all while the sprint is still running, so it runs to the
    /// as-of time. A closed sprint with neither time has no window: nothing says when it ended (owner decision D51).
    /// </summary>
    public static SprintWindow? Of(RawSprint sprint)
    {
        if (sprint.State == SprintStates.Future || sprint.Start is not { } start)
        {
            return null;
        }

        if (sprint.State != SprintStates.Closed)
        {
            return new SprintWindow(sprint.SprintId, start, sprint.Completed);
        }

        return (sprint.Completed ?? sprint.PlannedEnd) is { } end ? new SprintWindow(sprint.SprintId, start, end) : null;
    }

    /// <summary>True for a sprint that started and is closed, and whose end nothing gives: the one case of a started sprint without a window.</summary>
    public static bool IsClosedWithoutAnEnd(RawSprint sprint) =>
        sprint is { State: SprintStates.Closed, Start: not null, Completed: null, PlannedEnd: null };

    /// <summary>What is said of such a sprint, up to what follows from it for the command saying it.</summary>
    public static string ClosedWithoutAnEnd(string sprintId) =>
        $"Sprint {sprintId} is closed, but Jira gives it neither a completion time nor a planned end, so it has no sprint window";
}
