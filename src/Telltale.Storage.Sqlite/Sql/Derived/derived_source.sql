-- One row: the digest of the raw data this rebuild was derived from. When the raw data's digest
-- differs, the derived tables and the views are stale.
CREATE TABLE derived_source (
    raw_digest TEXT NOT NULL
) STRICT;
