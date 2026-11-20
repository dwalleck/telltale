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

-- Where a host's stored pull requests were read from, as the collection that last wrote them recorded it: for
-- GitHub the address of the REST API (github.apiUrl, or the public one), for Azure DevOps the address of the
-- organization (ado.apiUrl and ado.organization together). A pull request is one by host, repository and
-- number, so rows read from one server or organization must not be continued from another: while the
-- configured source differs from this one every command but backfill refuses, and a backfill removes the
-- host's pull requests, events and past closures before it reads the host whole. A read that failed leaves
-- it as it was. Null in rows written before this column existed: the source is then taken to be the
-- configured one, and the next read of the host records it.
ALTER TABLE pull_request_host ADD COLUMN source TEXT NULL;
