using System.Globalization;
using Telltale.Core.Storage;

namespace Telltale.Core.Derive;

/// <summary>
/// What is refused of the baseline sprint list (the <c>baselineSprintIds</c> setting), by <c>derive</c> and by
/// <c>report</c>. Every message names the setting and the sprints. The IDs are digits (the configuration's
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
    /// Stops when a baseline sprint started before <paramref name="completeFrom"/>, the start date the store was
    /// last filled from (null when the store names none). The store holds only issues updated since that date,
    /// so issues delivered in such a sprint and not touched since are missing, and a baseline pooled from it
    /// would be too low without a sign.
    /// </summary>
    public static void RequireWithinWhatWasFetched(IReadOnlyList<RawSprint> sprints, IReadOnlySet<string> baselineSprintIds, DateTimeOffset? completeFrom)
    {
        var tooEarly = sprints
            .Where(sprint => baselineSprintIds.Contains(sprint.SprintId) && sprint.Start < completeFrom)
            .Select(sprint => sprint.SprintId)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (tooEarly.Count > 0)
        {
            var one = tooEarly.Count == 1;
            throw new UserFacingException(
                $"baselineSprintIds in the configuration file lists {string.Join(", ", tooEarly)}, which {(one ? "started" : "each started")} before {completeFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}, the jira.backfillStart the store was last filled from, "
                + $"so issues delivered in {(one ? "it" : "them")} can be missing and the baseline would be too low. "
                + $"Take {(one ? "it" : "them")} out of the list, or set jira.backfillStart to a date before {(one ? "it" : "they")} started and run backfill.");
        }
    }

    /// <summary>
    /// The refusal of a list that holds sprints with fewer than five distinct assignees (owner decision D47).
    /// Such a sprint's own report withholds everything (GLOSSARY: Minimum group size), and a baseline pooled
    /// from it would give some of that back, so it cannot be a baseline sprint. The message names the setting
    /// and the sprints and says nothing else about them.
    /// </summary>
    public static UserFacingException ShortOfFiveAssignees(IReadOnlyList<string> sprintIds) =>
        new($"baselineSprintIds in the configuration file lists {string.Join(", ", sprintIds)}, which {(sprintIds.Count == 1 ? "has" : "each have")} fewer than five assignees. "
            + $"A sprint with fewer than five assignees cannot be a baseline sprint: take {(sprintIds.Count == 1 ? "it" : "them")} out of the list and run again.");
}
