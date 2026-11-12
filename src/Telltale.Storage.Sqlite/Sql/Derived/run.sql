-- One row: what this rebuild was built from. as_of is the time it used to close open intervals and the
-- running sprint's window. raw_digest identifies the raw data it read; when the raw data's digest differs,
-- the derived tables and the views are stale. time_zone and holidays are the settings its business days
-- were counted with (holidays as ISO dates in order, separated by commas).
CREATE TABLE run (
    as_of      TEXT NOT NULL,
    raw_digest TEXT NOT NULL,
    time_zone  TEXT NOT NULL,
    holidays   TEXT NOT NULL
) STRICT;
