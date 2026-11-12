-- One row: what this rebuild was built from. as_of is the time it used to close open intervals and the
-- running sprint's window. raw_digest identifies the raw data it read; when the raw data's digest differs,
-- the derived tables and the views are stale. time_zone and holidays are the settings its business days
-- were counted with (holidays as ISO dates in order, separated by commas). baseline_sprint_ids is the
-- baseline sprint list (GLOSSARY: Baseline) as a JSON array of the IDs in order, '[]' when there is no
-- baseline: the baseline views pool over it, and never read the configuration. objects_version identifies the
-- scripts that created the derived tables and the metric views: when the tool's scripts differ, the views
-- may lack a rule the tool relies on (the minimum group size, ADR-0003), and nothing is read from them.
CREATE TABLE run (
    as_of               TEXT NOT NULL,
    raw_digest          TEXT NOT NULL,
    time_zone           TEXT NOT NULL,
    holidays            TEXT NOT NULL,
    baseline_sprint_ids TEXT NOT NULL,
    objects_version     TEXT NOT NULL
) STRICT;
