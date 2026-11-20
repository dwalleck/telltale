-- One row per pull request and measure that has an observed wait (PullRequestTiming.cs): from the pull
-- request's first opening to the event that ended the wait. Only pull requests of the configured repositories
-- are here. measure is one of pr_measure's. sprint_id is the sprint whose window holds ended; null when no
-- window or more than one holds it, and such a wait is in no sprint's figures. elapsed_days is the whole wait
-- divided by 24 hours and business_days the same wait in business days; neither is clipped to a sprint.
-- author_hash is the pull request's author as a salted hash, read only to count the people behind a figure.
CREATE TABLE pr_wait (
    host          TEXT NOT NULL,
    repository    TEXT NOT NULL,
    pr_number     INTEGER NOT NULL,
    measure       TEXT NOT NULL,
    author_hash   TEXT NULL,
    opened        TEXT NOT NULL,
    ended         TEXT NOT NULL,
    elapsed_days  REAL NOT NULL,
    business_days REAL NOT NULL,
    sprint_id     TEXT NULL,
    PRIMARY KEY (host, repository, pr_number, measure)
) STRICT;
