-- At most one row: a digest that identifies the content of the raw tables (issue, change, sprint, status).
-- It is written in the same transaction as every batch, so it is always the digest of what the tables hold,
-- and equal raw data gives an equal digest however it was written. Derive records it (run.raw_digest), and
-- a report compares the two with one read instead of reading every raw row. No row until the first batch.
CREATE TABLE raw_state (
    digest TEXT NOT NULL
) STRICT;
