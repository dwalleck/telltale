-- A closing of a pull request that the store saw and that no longer stands: a collection found the pull
-- request listed with no closing time (reopened on GitHub, reactivated on Azure DevOps) or with another one
-- (opened and closed again since), where pull_request.closed held this one. pull_request keeps only the
-- closing that stands now, so without this row a sprint that closed while the pull request was closed would
-- be rebuilt as if it had been open. closed is the closing time the store held. reopened_by is the time of
-- the collection that found it gone, by the collectors' clock: the hosts' lists do not say when a pull request
-- was opened again, so all that is known is that it was before that collection. A closing that was made and
-- undone between two collections was never seen and has no row. Rows are added and never changed; they are
-- raw facts, kept by a backfill like the pull requests themselves.
CREATE TABLE pull_request_past_closure (
    host        TEXT NOT NULL,
    repository  TEXT NOT NULL,
    pr_number   INTEGER NOT NULL,
    closed      TEXT NOT NULL,
    reopened_by TEXT NOT NULL,
    PRIMARY KEY (host, repository, pr_number, closed)
) STRICT;
