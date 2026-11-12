-- One row per collector. covers says what was asked for (board, projects, start date): the watermark is
-- only true for that. updated_through is the newest update fetched, null when nothing has been fetched yet.
-- The row is written in the same transaction as the batch it describes, so it never runs ahead of the data.
CREATE TABLE watermark (
    collector       TEXT PRIMARY KEY,
    covers          TEXT NOT NULL,
    updated_through TEXT NULL
) STRICT;
