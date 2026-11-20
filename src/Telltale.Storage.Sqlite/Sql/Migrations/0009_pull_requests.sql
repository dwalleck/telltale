-- Pull requests, from any host (ADR-0002: selected fields only). A pull request is identified by its host
-- ('github'), its repository as the host's collector names it, and the host's number for it. No title, branch
-- name or login is kept; author_hash is the author as a salted hash (ADR-0003), null when the host names none.
-- merged is null unless it was merged; closed is null while it is open. updated is when the host says the pull
-- request last changed, null when it does not say: a sync reads a pull request's events again only when this,
-- merged or closed differ from what the host lists now.
CREATE TABLE pull_request (
    host        TEXT NOT NULL,
    repository  TEXT NOT NULL,
    pr_number   INTEGER NOT NULL,
    opened      TEXT NOT NULL,
    merged      TEXT NULL,
    closed      TEXT NULL,
    updated     TEXT NULL,
    author_hash TEXT NULL,
    PRIMARY KEY (host, repository, pr_number)
) STRICT;

-- The events on a pull request that can end a wait, classified by the collector when first fetched: kind
-- ('review'), outcome ('approved', 'changes requested', 'commented', 'dismissed', 'unknown') and actor
-- ('human', 'author', 'ops', 'automation', 'unknown'). Who the actor is is not kept in any form. A row is
-- written once and never changed: event_id is the host's ID of the event within its kind.
CREATE TABLE pull_request_event (
    host       TEXT NOT NULL,
    repository TEXT NOT NULL,
    pr_number  INTEGER NOT NULL,
    kind       TEXT NOT NULL,
    event_id   TEXT NOT NULL,
    outcome    TEXT NOT NULL,
    actor      TEXT NOT NULL,
    at         TEXT NOT NULL,
    PRIMARY KEY (host, repository, pr_number, kind, event_id)
) STRICT;

-- One row per pull request host that was ever read: what the last collection came to. state is 'read' or
-- 'failed'; roster is how the read of the Ops group's members ended: 'read', 'failed' or 'not configured'.
-- A host without a row was never read; whether a host is configured is the configuration's to say.
CREATE TABLE pull_request_host (
    host   TEXT PRIMARY KEY,
    state  TEXT NOT NULL,
    roster TEXT NOT NULL
) STRICT;
