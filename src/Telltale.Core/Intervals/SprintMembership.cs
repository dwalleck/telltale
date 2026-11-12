namespace Telltale.Core.Intervals;

/// <summary>One change to the issue's Sprint field: the set of sprint IDs before and after.</summary>
public sealed record SprintChange(string HistoryId, DateTimeOffset At, IReadOnlySet<string> FromSprintIds, IReadOnlySet<string> ToSprintIds)
{
    /// <summary>Builds a change from the changelog item's raw from/to values, e.g. "41, 42".</summary>
    public static SprintChange FromRaw(string historyId, DateTimeOffset at, string? from, string? to) =>
        new(historyId, at, SprintMembership.ParseSprintIds(from), SprintMembership.ParseSprintIds(to));
}

/// <summary>A period an issue belonged to one sprint. A null End means it still does.</summary>
public sealed record MembershipInterval(string SprintId, DateTimeOffset Start, DateTimeOffset? End)
{
    public Interval Resolve(DateTimeOffset asOf) => new(Start, End ?? (asOf > Start ? asOf : Start));
}

/// <summary>
/// When an issue belonged to which sprint, rebuilt from the Sprint field's history.
/// Use this to decide which slice of an issue's history counts toward a sprint, rather
/// than current sprint membership, which drops issues removed mid-sprint.
/// </summary>
/// <remarks>
/// When Jira carries an unfinished issue into the next sprint, the field usually goes
/// from "41" to "41, 42": the closed sprint stays listed. So membership in a closed
/// sprint often never ends in the changelog. That's expected; clipping to the sprint's
/// own window (see <see cref="TimeInStatus"/>) bounds it.
/// </remarks>
public sealed record SprintMembership(IReadOnlyList<MembershipInterval> Intervals, IReadOnlyList<string> Anomalies)
{
    private static readonly IReadOnlySet<string> NoSprints = new HashSet<string>();

    public IEnumerable<MembershipInterval> ForSprint(string sprintId) =>
        Intervals.Where(interval => interval.SprintId == sprintId);

    public static IReadOnlySet<string> ParseSprintIds(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? NoSprints
            : raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    public static SprintMembership Build(DateTimeOffset created, IReadOnlySet<string> currentSprintIds, IEnumerable<SprintChange> changes)
    {
        var ordered = ChangeOrdering.DedupeAndSort(changes, c => c.HistoryId, c => c.At);
        var intervals = new List<MembershipInterval>();
        var anomalies = new List<string>();

        var initial = ordered.Count == 0 ? currentSprintIds : ordered[0].FromSprintIds;
        var open = initial.ToDictionary(id => id, _ => created, StringComparer.Ordinal);

        foreach (var change in ordered)
        {
            if (!change.FromSprintIds.SetEquals(open.Keys))
            {
                anomalies.Add($"History {change.HistoryId}: Sprint field changed from [{Join(change.FromSprintIds)}] but the timeline had [{Join(open.Keys)}].");
            }

            var at = change.At < created ? created : change.At;

            foreach (var removed in open.Keys.Where(id => !change.ToSprintIds.Contains(id)).ToList())
            {
                if (at > open[removed])
                {
                    intervals.Add(new MembershipInterval(removed, open[removed], at));
                }

                open.Remove(removed);
            }

            foreach (var added in change.ToSprintIds.Where(id => !open.ContainsKey(id)))
            {
                open[added] = at;
            }
        }

        foreach (var (sprintId, start) in open.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            intervals.Add(new MembershipInterval(sprintId, start, null));
        }

        if (!currentSprintIds.SetEquals(open.Keys))
        {
            anomalies.Add($"Timeline ends in sprints [{Join(open.Keys)}] but the issue's Sprint field is [{Join(currentSprintIds)}].");
        }

        return new SprintMembership(intervals, anomalies);
    }

    private static string Join(IEnumerable<string> ids) => string.Join(", ", ids.Order(StringComparer.Ordinal));
}
