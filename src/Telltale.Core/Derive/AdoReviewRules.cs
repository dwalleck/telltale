using Telltale.Core.Storage;

namespace Telltale.Core.Derive;

/// <summary>
/// Which events of an Azure DevOps pull request end a wait. The events are the votes and the text comments on
/// files the collector stored, each classified when it was first fetched (<see cref="RawPullRequestEvent"/>):
/// general conversation, vote resets and a reviewer's current vote, which has no time, were never stored.
/// There is no Ops group on Azure DevOps: only the author's own activity and automation are passed over.
/// </summary>
public static class AdoReviewRules
{
    /// <summary>
    /// The first code review: the earliest vote other than a reset, or text comment on a file, by someone who
    /// is neither the author nor automation, made while the pull request was not yet closed. A vote against
    /// counts as much as one in favour. An earlier event that cannot be classified could have been the first,
    /// so the pull request then has no first code review that can be relied on: null, as for one that has none.
    /// </summary>
    /// <param name="events">The pull request's events, earliest first.</param>
    public static DateTimeOffset? FirstCodeReview(RawPullRequest pullRequest, IReadOnlyList<RawPullRequestEvent> events)
    {
        foreach (var happened in BeforeClosure(pullRequest, events))
        {
            switch (happened.Actor)
            {
                case PullRequestEventActors.Author or PullRequestEventActors.Automation:
                    continue;
                case PullRequestEventActors.Human when happened.Outcome != PullRequestEventOutcomes.Unknown:
                    return happened.At;
                default:
                    return null;
            }
        }

        return null;
    }

    /// <summary>
    /// The first code approval: the earliest positive vote (approved, or approved with suggestions) by someone
    /// who is neither the author nor automation, made while the pull request was not yet closed. Comments and
    /// votes against are passed over: they are reviews, not approvals. An earlier vote that could have been
    /// such an approval and cannot be told (its voter or its value is unknown) leaves the pull request with no
    /// first code approval that can be relied on.
    /// </summary>
    /// <param name="events">The pull request's events, earliest first.</param>
    public static DateTimeOffset? FirstCodeApproval(RawPullRequest pullRequest, IReadOnlyList<RawPullRequestEvent> events)
    {
        foreach (var vote in BeforeClosure(pullRequest, events).Where(happened => happened.Kind == PullRequestEventKinds.Vote))
        {
            switch (vote.Actor, vote.Outcome)
            {
                case (PullRequestEventActors.Author or PullRequestEventActors.Automation, _):
                    continue;
                case (PullRequestEventActors.Human, PullRequestEventOutcomes.Approved):
                    return vote.At;
                case (_, PullRequestEventOutcomes.Approved or PullRequestEventOutcomes.Unknown):
                    return null;
                default:
                    continue;
            }
        }

        return null;
    }

    /// <summary>The events made while the pull request was not yet closed: what came after its closing is not review of it.</summary>
    private static IEnumerable<RawPullRequestEvent> BeforeClosure(RawPullRequest pullRequest, IReadOnlyList<RawPullRequestEvent> events) =>
        events.Where(happened => happened.Kind is PullRequestEventKinds.Vote or PullRequestEventKinds.FileComment)
            .TakeWhile(happened => pullRequest.Closed is not { } closed || happened.At <= closed);
}
