-- One row: what this rebuild was made from. raw_digest is the digest of the raw data: when the raw data's
-- digest differs, the derived tables and the views are stale. objects_version identifies the scripts that
-- created the derived tables and the metric views: when the tool's scripts differ, the views may lack a
-- rule the tool relies on (the minimum group size, ADR-0003), and nothing is read from them.
CREATE TABLE derived_source (
    raw_digest      TEXT NOT NULL,
    objects_version TEXT NOT NULL
) STRICT;
