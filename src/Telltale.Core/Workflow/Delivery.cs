using Telltale.Core.Intervals;

namespace Telltale.Core.Workflow;

/// <summary>The issue is in a done-role status. <paramref name="At"/> is its last move from a status that is not done into one that is.</summary>
public sealed record Delivered(DateTimeOffset At)
{
    /// <summary>The changelog entry of that move. It places the delivery among other changes made at the same instant.</summary>
    public string? HistoryId { get; init; }
}

/// <summary>
/// The issue is in a done-role status, but its resolution is one of the configured abandoned resolutions:
/// it was closed without being done. <paramref name="At"/> is its last move into done, as for <see cref="Delivered"/>.
/// </summary>
public sealed record Abandoned(DateTimeOffset At);

/// <summary>The issue is not in a done-role status now, or its history holds no move into one.</summary>
public sealed record NotDelivered;

/// <summary>
/// What became of an issue. Consume it with a switch expression that lists every case and has no
/// discard arm, so a new case is a compile error wherever an outcome is read.
/// </summary>
public union IssueOutcome(Delivered, Abandoned, NotDelivered);

public static class Delivery
{
    /// <summary>
    /// Decides an issue's outcome from the status and resolution it has now, the statuses it entered
    /// (<see cref="StatusTimeline.Entries"/>, rebuilt from its changelog, in time order), the configured status
    /// roles and the configured abandoned resolutions.
    /// </summary>
    /// <remarks>
    /// The issue's current status decides whether it is done; the changelog only dates the delivery.
    /// When the two disagree (the changelog ends in a status the issue is not in), or the changelog holds
    /// no move into done at all, there is no delivery moment and the issue is not delivered.
    /// The entries are read, not the status intervals, because a status entered and left at the same instant
    /// has no interval and is still a move: a reopen closed again in the same instant moves the delivery there.
    /// The resolution only tells an abandoned issue from a delivered one. An issue with no resolution, or with
    /// one that is not in the abandoned list, is delivered.
    /// </remarks>
    public static IssueOutcome Of(
        string currentStatusId,
        string? currentResolutionId,
        IReadOnlyList<StatusEntry> entries,
        StatusRoles roles,
        IReadOnlySet<string> abandonedResolutionIds)
    {
        if (!roles.IsDone(currentStatusId) || entries.Count == 0 || !roles.IsDone(entries[^1].StatusId))
        {
            return new NotDelivered();
        }

        // Walk back over the unbroken run of done statuses the issue is in now. The run's start is
        // the last move into done from outside it: Done to Shipped stays inside the run, and an
        // earlier Done that was reopened lies before it.
        var first = entries.Count - 1;
        while (first > 0 && roles.IsDone(entries[first - 1].StatusId))
        {
            first--;
        }

        // A run that reaches back to the issue's creation was never entered: nothing moved the issue into done.
        if (first == 0)
        {
            return new NotDelivered();
        }

        return currentResolutionId is not null && abandonedResolutionIds.Contains(currentResolutionId)
            ? new Abandoned(entries[first].At)
            : new Delivered(entries[first].At) { HistoryId = entries[first].HistoryId };
    }
}
