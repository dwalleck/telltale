namespace Telltale.Core.Intervals;

/// <summary>One status transition from the issue's changelog. Status IDs, not names, since names get renamed.</summary>
public sealed record StatusChange(string HistoryId, DateTimeOffset At, string? FromStatusId, string ToStatusId);

/// <summary>A period an issue spent in one status. A null End means it is still in that status.</summary>
public sealed record StatusInterval(string StatusId, DateTimeOffset Start, DateTimeOffset? End)
{
    /// <summary>Closes an open interval at <paramref name="asOf"/>.</summary>
    public Interval Resolve(DateTimeOffset asOf) => new(Start, End ?? (asOf > Start ? asOf : Start));
}

/// <summary>The moment an issue entered a status: at its creation, or by a transition.</summary>
public sealed record StatusEntry(string StatusId, DateTimeOffset At)
{
    /// <summary>The changelog entry of the transition; null for the status the issue was created in.</summary>
    public string? HistoryId { get; init; }

    /// <summary>
    /// True when the transition was dated before the issue was created, so that <see cref="At"/> is the creation
    /// time it is treated as made at and not its own date (a changelog anomaly).
    /// </summary>
    public bool MovedToCreation { get; init; }
}

/// <summary>
/// An issue's full status history as back-to-back intervals, from creation to now.
/// Anomalies record places where the changelog doesn't line up; the timeline is still
/// built, following each change's target status.
/// </summary>
/// <remarks>
/// <see cref="Intervals"/> answers "how long": a status entered and left at the same instant took no time and has
/// no interval. <see cref="Entries"/> answers "what happened": it holds every status the issue entered, in order,
/// starting with the one it was created in, so such a visit is still there. Rules about transitions read the entries.
/// </remarks>
public sealed record StatusTimeline(IReadOnlyList<StatusInterval> Intervals, IReadOnlyList<StatusEntry> Entries, IReadOnlyList<string> Anomalies)
{
    public static StatusTimeline Build(DateTimeOffset created, string currentStatusId, IEnumerable<StatusChange> changes)
    {
        var ordered = ChangeOrdering.DedupeAndSort(changes, c => c.HistoryId, c => c.At);
        var intervals = new List<StatusInterval>();
        var anomalies = new List<string>();

        // With no transitions, the issue has been in its current status since it was created.
        // Otherwise the first transition says what status the issue was created in.
        var current = ordered.Count == 0
            ? currentStatusId
            : ordered[0].FromStatusId ?? ordered[0].ToStatusId;
        var start = created;
        var entries = new List<StatusEntry> { new(current, created) };

        foreach (var change in ordered)
        {
            if (change.FromStatusId is { } from && from != current)
            {
                anomalies.Add($"History {change.HistoryId}: moved from '{from}' but the timeline was in '{current}'.");
            }

            var at = change.At;
            var movedToCreation = at < start;
            if (movedToCreation)
            {
                anomalies.Add($"History {change.HistoryId}: dated {at:o}, before the issue was created; treated as {start:o}.");
                at = start;
            }

            if (at > start)
            {
                intervals.Add(new StatusInterval(current, start, at));
            }

            entries.Add(new StatusEntry(change.ToStatusId, at) { HistoryId = change.HistoryId, MovedToCreation = movedToCreation });
            current = change.ToStatusId;
            start = at;
        }

        intervals.Add(new StatusInterval(current, start, null));

        if (current != currentStatusId)
        {
            anomalies.Add($"Timeline ends in '{current}' but the issue's current status is '{currentStatusId}'.");
        }

        return new StatusTimeline(intervals, entries, anomalies);
    }
}
