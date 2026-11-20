using Telltale.Core.Storage;

namespace Telltale.Core.Derive;

/// <summary>
/// Which events of a GitHub pull request end a wait. The events are the reviews the collector stored, each
/// classified when it was first fetched (<see cref="RawPullRequestEvent"/>): ordinary conversation and pending
/// reviews were never stored.
/// </summary>
public static class GitHubReviewRules
{
    /// <summary>
    /// The first code review (GLOSSARY: Time to first code review): the earliest submitted review by a person
    /// who is neither the author nor in the Ops group, made while the pull request was not yet closed. A
    /// comment-only review, a request for changes and an approval all count, and so does one dismissed since.
    /// Reviews by the author, automation and the Ops group are passed over. An earlier review that cannot be
    /// classified could have been the first, so the pull request then has no first code review that can be
    /// relied on: null, as for one that has none.
    /// </summary>
    /// <param name="events">The pull request's events, earliest first.</param>
    public static DateTimeOffset? FirstCodeReview(RawPullRequest pullRequest, IReadOnlyList<RawPullRequestEvent> events)
    {
        foreach (var review in events.Where(happened => happened.Kind == PullRequestEventKinds.Review))
        {
            if (pullRequest.Closed is { } closed && review.At > closed)
            {
                return null;
            }

            switch (review.Actor)
            {
                case PullRequestEventActors.Author or PullRequestEventActors.Ops or PullRequestEventActors.Automation:
                    continue;
                case PullRequestEventActors.Human when review.Outcome != PullRequestEventOutcomes.Unknown:
                    return review.At;
                default:
                    return null;
            }
        }

        return null;
    }

    /// <summary>
    /// The first code approval (GLOSSARY: Time to first code approval): the earliest approving review by a
    /// person who is neither the author nor in the Ops group, made while the pull request was not yet closed.
    /// It can be the first code review as well, and then ends both waits at the same moment. A comment-only
    /// review and a request for changes are reviews and not approvals, whoever made them, and are passed over,
    /// as are all reviews by the author, automation and the Ops group: an approval by the Ops group is kept as
    /// its own class and is never a code approval.
    /// </summary>
    /// <param name="events">The pull request's events, earliest first.</param>
    public static DateTimeOffset? FirstCodeApproval(RawPullRequest pullRequest, IReadOnlyList<RawPullRequestEvent> events)
    {
        foreach (var review in events.Where(happened => happened.Kind == PullRequestEventKinds.Review))
        {
            if (pullRequest.Closed is { } closed && review.At > closed)
            {
                return null;
            }

            if (review is { Actor: PullRequestEventActors.Human, Outcome: PullRequestEventOutcomes.Approved })
            {
                return review.At;
            }
        }

        return null;
    }
}
