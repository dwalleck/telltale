using Telltale.Core.Storage;

namespace Telltale.Core.Derive;

/// <summary>
/// What is refused of the baseline sprint list (the <c>baselineSprintIds</c> setting), by <c>derive</c> and by
/// <c>report</c>, and what is only said about it. Every message names the setting and the sprints. The IDs are digits (the configuration's
/// checks), so a message can quote them.
/// </summary>
public static class BaselineSprintRules
{
    /// <summary>
    /// Stops when a baseline sprint ID is not a completed sprint in the store (which holds the configured
    /// board's sprints and no others). A
    /// mistyped ID would otherwise leave its sprint out of the baseline without a sign, and a sprint that is
    /// running or has not started would make the baseline move, or add nothing to it: the baseline is a fixed
    /// list of past sprints (GLOSSARY: Baseline).
    /// </summary>
    public static void RequireCompleted(IReadOnlyList<RawSprint> sprints, IReadOnlySet<string> baselineSprintIds)
    {
        var completed = sprints
            .Where(sprint => sprint.State == SprintStates.Closed)
            .Select(sprint => sprint.SprintId)
            .ToHashSet(StringComparer.Ordinal);
        var refused = baselineSprintIds.Where(sprintId => !completed.Contains(sprintId)).Order(StringComparer.Ordinal).ToList();
        if (refused.Count > 0)
        {
            throw new UserFacingException(
                $"baselineSprintIds in the configuration file lists {string.Join(", ", refused)}, and the store holds no completed sprint with such an ID. "
                + "The baseline is drawn from completed sprints only. Correct the list, or run backfill if the sprint has completed since, and run again.");
        }
    }

    /// <summary>
    /// Stops when a baseline sprint started before <paramref name="completeFrom"/> (<see cref="FetchedRange"/>):
    /// a baseline pooled from it would be too low without a sign. <paramref name="starts"/> is when each sprint
    /// started; a sprint that is not in it is not asked about.
    /// </summary>
    public static void RequireWithinWhatWasFetched(IEnumerable<(string SprintId, DateTimeOffset Start)> starts, IReadOnlySet<string> baselineSprintIds, DateTimeOffset? completeFrom)
    {
        var tooEarly = FetchedRange.StartedBefore(starts.Where(sprint => baselineSprintIds.Contains(sprint.SprintId)), completeFrom);
        if (tooEarly.Count > 0)
        {
            var one = tooEarly.Count == 1;
            throw new UserFacingException(
                $"baselineSprintIds in the configuration file lists {string.Join(", ", tooEarly)}, which {(one ? "started" : "each started")} before {FetchedRange.Date(completeFrom)}, the jira.backfillStart the store was last filled from, "
                + $"so issues delivered in {(one ? "it" : "them")} can be missing and the baseline would be too low. "
                + $"Take {(one ? "it" : "them")} out of the list, or set jira.backfillStart to a date before {(one ? "it" : "they")} started and run backfill.");
        }
    }

    /// <summary>
    /// Stops when a baseline sprint has no sprint window (<see cref="SprintWindows.Of"/>): Jira gives it no
    /// start date, or it is closed with neither a completion time nor a planned end (owner decision D51).
    /// Such a sprint is in no figure, so a baseline could not be pooled from it.
    /// </summary>
    public static void RequireWindow(IReadOnlyList<RawSprint> sprints, IReadOnlySet<string> baselineSprintIds)
    {
        var withoutWindow = sprints
            .Where(sprint => baselineSprintIds.Contains(sprint.SprintId) && SprintWindows.Of(sprint) is null)
            .Select(sprint => sprint.SprintId)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (withoutWindow.Count > 0)
        {
            var one = withoutWindow.Count == 1;
            throw new UserFacingException(
                $"baselineSprintIds in the configuration file lists {string.Join(", ", withoutWindow)}, which {(one ? "has" : "each have")} no sprint window: "
                + $"Jira gives {(one ? "it" : "them")} no start date, or no completion time and no planned end, so nothing says when {(one ? "it" : "they")} ran. "
                + $"A sprint without a window has no figures and cannot be a baseline sprint: take {(one ? "it" : "them")} out of the list and run again.");
        }
    }

    /// <summary>
    /// What the console says when baseline sprints have their delivered count withheld: fewer than five
    /// distinct assignees, or fewer than three among the delivered issues (owner decisions D50, D56). Nothing
    /// stops: the views withhold every baseline figure, and this names the sprints and says so. It does not
    /// say which of the two it is, which the sprint's own report shows.
    /// </summary>
    public static string DeliveredCountWithheld(IReadOnlyList<string> sprintIds) =>
        $"{SprintsNamed(sprintIds)} {(sprintIds.Count == 1 ? "has" : "each have")} fewer than five assignees, or fewer than three among {(sprintIds.Count == 1 ? "its" : "their")} delivered issues, "
        + "so every baseline figure is withheld (baselineSprintIds in the configuration file).";

    /// <summary>"Baseline sprint 41", or "Baseline sprints 41, 42 and 43".</summary>
    public static string SprintsNamed(IReadOnlyList<string> sprintIds) => sprintIds.Count == 1
        ? $"Baseline sprint {sprintIds[0]}"
        : $"Baseline sprints {string.Join(", ", sprintIds.Take(sprintIds.Count - 1))} and {sprintIds[^1]}";
}
