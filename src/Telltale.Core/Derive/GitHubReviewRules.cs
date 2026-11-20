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
    /// <remarks>
    /// A review keeps the outcome it was first stored with (<see cref="RawPullRequestEvent"/>), and GitHub does
    /// not say what a dismissed review had been. So a dismissal is read in two ways, by when it was seen:
    /// <list type="bullet">
    /// <item>A review stored as an approval and dismissed afterwards stays the approval it was: it was given,
    /// at that time, and a figure already reported does not change.</item>
    /// <item>A review already dismissed when it was first fetched is a review and no evidence of an approval.
    /// It may have been one, though, earlier than any approval after it, so the pull request then has no first
    /// code approval that can be relied on: null, as for one that has none. The later approval is not taken in
    /// its place, which would state a wait longer than the one that may have ended at the dismissed review.</item>
    /// </list>
    /// The same holds for any earlier review that may have been a person's approval and cannot be classified.
    /// </remarks>
    /// <param name="events">The pull request's events, earliest first.</param>
    public static DateTimeOffset? FirstCodeApproval(RawPullRequest pullRequest, IReadOnlyList<RawPullRequestEvent> events)
    {
        foreach (var review in events.Where(happened => happened.Kind == PullRequestEventKinds.Review))
        {
            if (pullRequest.Closed is { } closed && review.At > closed)
            {
                return null;
            }

            switch (review.Actor, review.Outcome)
            {
                case (PullRequestEventActors.Author or PullRequestEventActors.Ops or PullRequestEventActors.Automation, _):
                case (_, PullRequestEventOutcomes.Commented or PullRequestEventOutcomes.ChangesRequested):
                    continue;
                case (PullRequestEventActors.Human, PullRequestEventOutcomes.Approved):
                    return review.At;
                default:
                    return null;
            }
        }

        return null;
    }
}
