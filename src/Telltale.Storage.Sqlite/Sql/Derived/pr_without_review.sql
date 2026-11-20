-- One row per sprint and pull request of a configured repository that had no first code review when the
-- sprint closed, or at the as-of time for a sprint still running (PullRequestsWithoutReview.cs). A pull
-- request is looked at for a sprint when it was opened before that moment and was still open then or closed
-- during the sprint; what happened after that moment is left out. These rows explain a count and are no
-- waits: none has a duration. state is why the pull request has no first code review: 'waiting' (open),
-- 'closed without review' (closed in the sprint, merged or not), 'unclassifiable' (an event that could have
-- been the first code review cannot be classified) or 'history not read' (its events were not read at the
-- last collection). activity is what had been submitted on it before it closed: 'ops only' (something by the
-- Ops group), 'automation only' (something by automation and nothing by the Ops group) or 'none'.
-- The row carries no author: pull request results are not grouped or counted by person.
CREATE TABLE pr_without_review (
    sprint_id  TEXT NOT NULL,
    host       TEXT NOT NULL,
    repository TEXT NOT NULL,
    pr_number  INTEGER NOT NULL,
    state      TEXT NOT NULL,
    activity   TEXT NOT NULL,
    PRIMARY KEY (sprint_id, host, repository, pr_number)
) STRICT;
