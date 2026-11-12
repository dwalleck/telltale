-- One row per collector: every change up to updated_through is in the raw tables. It is written in the
-- same transaction as the batch it describes, so it never runs ahead of the data.
CREATE TABLE watermark (
    collector       TEXT PRIMARY KEY,
    updated_through TEXT NOT NULL
) STRICT;
