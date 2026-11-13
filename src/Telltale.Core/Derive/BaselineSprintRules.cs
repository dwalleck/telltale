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
    /// The baseline sprints whose delivered count is withheld (fewer distinct assignees than a team, or fewer
    /// than a figure needs among the delivered issues: five and three by default; owner decisions D50, D56), in
    /// order, read from the metric view
    /// <c>baseline_sprint</c>. While there is one, the views withhold every baseline figure, and nothing stops:
    /// the console and the report name the sprints, with <see cref="WithheldOnTheConsole"/> and
    /// <see cref="WithheldInTheReport"/>. Neither says which of the two reasons applies.
    /// </summary>
    public static Task<IReadOnlyList<string>> ReadWithheldAsync(ITelltaleStore store, CancellationToken cancellationToken = default) =>
        store.QueryViewsAsync<string>("SELECT sprint_id FROM baseline_sprint WHERE suppressed <> 0 ORDER BY sprint_id;", cancellationToken: cancellationToken);

    /// <summary>
    /// The console's line about <paramref name="sprintIds"/>, the result of <see cref="ReadWithheldAsync"/> when it
    /// is not empty. It gives the numbers in force (<see cref="NumbersInForce.ReadAsync"/>; owner decision D67),
    /// and of the two checks only those that apply.
    /// </summary>
    public static string WithheldOnTheConsole(IReadOnlyList<string> sprintIds, NumbersInForce numbers) =>
        $"{SprintsNamed(sprintIds)} {ShortOf(sprintIds, numbers)}, so every baseline figure is withheld (baselineSprintIds in the configuration file).";

    /// <summary>
    /// The method note's sentence about the same sprints. Under the default numbers it is the sentence it was
    /// before the numbers became a setting, which gives none (the sentence before it in the note does); under
    /// other numbers it gives them as the console's line does.
    /// </summary>
    public static string WithheldInTheReport(IReadOnlyList<string> sprintIds, NumbersInForce numbers) =>
        $"{SprintsNamed(sprintIds)} {(numbers.AreTheDefault ? CountWithheld(sprintIds) : ShortOf(sprintIds, numbers))}, so every baseline figure in this report reads \"suppressed\".";

    /// <summary>
    /// What the sprints are short of, by the checks that apply. With both checks off the views withhold no
    /// baseline sprint (a list with a sprint the views would still withhold is refused by derive); were one
    /// listed all the same, the sentence says what the view says and gives no number.
    /// </summary>
    private static string ShortOf(IReadOnlyList<string> sprintIds, NumbersInForce numbers)
    {
        var (has, its) = sprintIds.Count == 1 ? ("has", "its") : ("each have", "their");
        return (numbers.TeamCheck, numbers.FigureCheck) switch
        {
            (true, true) => $"{has} fewer than {numbers.TeamAssignees()}, or fewer than {numbers.BehindAFigureInWords} among {its} delivered issues",
            (true, false) => $"{has} fewer than {numbers.TeamAssignees()}",
            (false, true) => $"{has} fewer than {numbers.AssigneesBehindAFigure()} among {its} delivered issues",
            (false, false) => CountWithheld(sprintIds),
        };
    }

    /// <summary>"has its delivered count withheld", or "each have their delivered count withheld".</summary>
    private static string CountWithheld(IReadOnlyList<string> sprintIds) =>
        $"{(sprintIds.Count == 1 ? "has its" : "each have their")} delivered count withheld";

    /// <summary>"Baseline sprint 41", or "Baseline sprints 41, 42 and 43".</summary>
    private static string SprintsNamed(IReadOnlyList<string> sprintIds) => sprintIds.Count == 1
        ? $"Baseline sprint {sprintIds[0]}"
        : $"Baseline sprints {string.Join(", ", sprintIds.Take(sprintIds.Count - 1))} and {sprintIds[^1]}";
}
