-- A period an issue belonged to a sprint, rebuilt from the Sprint field's history. ended is null while
-- the field still lists the sprint, which is usual for a closed sprint: Jira keeps it listed.
CREATE TABLE membership_interval (
    issue_id  TEXT NOT NULL,
    sprint_id TEXT NOT NULL,
    started   TEXT NOT NULL,
    ended     TEXT NULL
) STRICT;
