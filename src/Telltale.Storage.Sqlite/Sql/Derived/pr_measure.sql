-- The measures a pull request can have a wait for, in the order a report lists them (PullRequestTiming.cs).
-- Empty when no pull request host is configured: pr_timing_per_sprint then has no rows.
CREATE TABLE pr_measure (
    measure  TEXT PRIMARY KEY,
    position INTEGER NOT NULL
) STRICT;
