using Telltale.Core.Intervals;
using Telltale.Core.Storage;

namespace Telltale.Core.Derive;

/// <summary>
/// The pull requests that give a sprint no first code review, and why: what explains the number of pull
/// requests behind the first code review figure. They are counted, never timed: none of them becomes a wait,
/// of zero or of any other length.
/// </summary>
/// <remarks>
/// Each sprint is looked at as of its close, or as of the rebuild's as-of time while it is still running:
/// what happened later is left out, so a review made after the close does not take the pull request out of
/// the sprint's count of those that were waiting when it closed. A pull request is looked at for a sprint
/// when it was opened before that moment and was either still open then or closed during the sprint; one
/// closed before the sprint started is no longer the sprint's to explain. Whether it was closed at that
/// moment is not only what its row says now: a pull request reopened since was closed then, as far as the
/// store saw it (<see cref="RawPullRequestPastClosure"/>). Of those, one that had its first
/// code review by then is not here. Every other one is in exactly one <see cref="PullRequestReviewStates"/>.
/// </remarks>
public static class PullRequestsWithoutReview
{
    /// <summary>
    /// One row per sprint window and pull request of a configured repository that had no first code review at
    /// the sprint's close, as of <paramref name="asOf"/>.
    /// </summary>
    public static IReadOnlyList<PullRequestWithoutReview> Of(
        RawData raw,
        IReadOnlyDictionary<string, IReadOnlyList<string>> repositoriesByHost,
        IReadOnlyList<SprintWindow> windows,
        DateTimeOffset asOf)
    {
        var counted = raw.PullRequests
            .Where(pullRequest => repositoriesByHost.TryGetValue(pullRequest.Host, out var repositories) && repositories.Contains(pullRequest.Repository, StringComparer.Ordinal))
            .ToList();
        var events = raw.PullRequestEvents.ToLookup(happened => (happened.Host, happened.Repository, happened.Number));
        var pastClosures = raw.PullRequestPastClosures.ToLookup(closure => (closure.Host, closure.Repository, closure.Number));
        var rows = new List<PullRequestWithoutReview>();
        foreach (var window in windows)
        {
            // The moment the sprint is looked at: its close, and never later than the as-of time.
            var at = window.Completed is { } completed && completed < asOf ? completed : asOf;

            // A sprint can be started in Jira with a start that is still to come. Its window has not begun at
            // that moment, so no pull request is its to explain yet: it holds no wait either.
            if (at <= window.Start)
            {
                continue;
            }

            foreach (var pullRequest in counted)
            {
                // The closing the pull request was under at that moment, if it was closed then: the one that
                // stands now, or one it was reopened from since. One closed before the sprint started, and
                // not open again by that moment, is not the sprint's to explain.
                var closed = ClosedAt(at, pullRequest, pastClosures[(pullRequest.Host, pullRequest.Repository, pullRequest.Number)]);
                if (pullRequest.Opened >= at || closed < window.Start)
                {
                    continue;
                }

                // What the pull request was at that moment: a merge or a close dated later had not happened,
                // and a reopening seen later had not either.
                var seen = pullRequest with
                {
                    Merged = pullRequest.Merged < at ? pullRequest.Merged : null,
                    Closed = closed,
                };
                var itsEvents = events[(pullRequest.Host, pullRequest.Repository, pullRequest.Number)]
                    .Where(happened => happened.At < at)
                    .OrderBy(happened => happened.At)
                    .ThenBy(happened => happened.EventId, StringComparer.Ordinal)
                    .ToList();
                if (StateOf(seen, itsEvents) is var (state, activity))
                {
                    rows.Add(new PullRequestWithoutReview(window.SprintId, seen.Host, seen.Repository, seen.Number, state, activity));
                }
            }
        }

        return rows;
    }

    /// <summary>
    /// When the pull request was closed, if it was closed at the moment <paramref name="at"/>; null if it was
    /// open then. It was closed then under the closing that stands now, when that is before the moment, or
    /// under a past one (<see cref="RawPullRequestPastClosure"/>) from its closing until the collection that
    /// saw the pull request open again. The store does not know when in that time it was reopened, so a
    /// moment after the reopening and before that collection still finds it closed: the sprint is told as the
    /// store saw it then. Where two closings hold the moment (a pull request closed again between two
    /// collections), the later one is the one it was under.
    /// </summary>
    private static DateTimeOffset? ClosedAt(DateTimeOffset at, RawPullRequest pullRequest, IEnumerable<RawPullRequestPastClosure> pastClosures)
    {
        var closings = pastClosures.Where(closure => closure.Closed < at && at < closure.ReopenedBy).Select(closure => closure.Closed);
        if (pullRequest.Closed is { } standing && standing < at)
        {
            closings = closings.Append(standing);
        }

        return closings.Select(closing => (DateTimeOffset?)closing).Max();
    }

    /// <summary>Why the pull request has no first code review, and what had been submitted on it; null when it has one.</summary>
    private static (string State, string Activity)? StateOf(RawPullRequest seen, IReadOnlyList<RawPullRequestEvent> events)
    {
        // What was submitted while the pull request was not yet closed: what came after is not review of it.
        var beforeClosing = events.Where(happened => seen.Closed is not { } closed || happened.At <= closed).ToList();
        var activity = beforeClosing.Any(happened => happened.Actor == PullRequestEventActors.Ops) ? PullRequestReviewActivities.OpsOnly
            : beforeClosing.Any(happened => happened.Actor == PullRequestEventActors.Automation) ? PullRequestReviewActivities.AutomationOnly
            : PullRequestReviewActivities.None;

        // A first code review the stored events already give by that moment stands, whatever the row's
        // history says now: stored events are from reads that ended and are never deleted, so an event that
        // was not read since is later than all of them and cannot come before it. A read that fails today
        // therefore does not put the pull request back into the sprints after its review.
        if (PullRequestTiming.FirstCodeReviewOf(seen, events) is not null)
        {
            return null;
        }

        // The stored events give none, and events that were not read can hold the first code review: nothing
        // more is said of what the read ones show.
        if (seen.History != PullRequestHistoryStates.Read)
        {
            return (PullRequestReviewStates.HistoryNotRead, activity);
        }

        // The host's rule found no first code review. It stops at an event it cannot classify, which could
        // have been the first: that is not the same as a pull request nobody reviewed.
        if (beforeClosing.Any(happened => happened.Actor == PullRequestEventActors.Unknown
            || (happened.Actor == PullRequestEventActors.Human && happened.Outcome == PullRequestEventOutcomes.Unknown)))
        {
            return (PullRequestReviewStates.Unclassifiable, activity);
        }

        return (seen.Closed is null ? PullRequestReviewStates.Waiting : PullRequestReviewStates.ClosedWithoutReview, activity);
    }
}

/// <summary>The values of <see cref="PullRequestWithoutReview.State"/>: a pull request is in exactly one.</summary>
public static class PullRequestReviewStates
{
    /// <summary>Open at the sprint's close, with no first code review yet.</summary>
    public const string Waiting = "waiting";

    /// <summary>Closed during the sprint, merged or not, with no first code review before it closed.</summary>
    public const string ClosedWithoutReview = "closed without review";

    /// <summary>An event that could have been the first code review cannot be classified, so the pull request has no first code review that can be relied on.</summary>
    public const string Unclassifiable = "unclassifiable";

    /// <summary>The pull request's events were not read at the last collection (<see cref="PullRequestHistoryStates"/>): whether it was reviewed is not known.</summary>
    public const string HistoryNotRead = "history not read";
}

/// <summary>
/// The values of <see cref="PullRequestWithoutReview.Activity"/>: what had been submitted on the pull request,
/// before it closed, by anyone but a person who could give a code review. Exactly one applies.
/// </summary>
public static class PullRequestReviewActivities
{
    /// <summary>Nothing, or only the author's own.</summary>
    public const string None = "none";

    /// <summary>Something by automation, and nothing by the Ops group.</summary>
    public const string AutomationOnly = "automation only";

    /// <summary>Something by the Ops group (GitHub only), whatever automation did besides.</summary>
    public const string OpsOnly = "ops only";
}
