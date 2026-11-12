-- The sprints that count: sprints that have started (the store holds the configured board's only). Derive decides which
-- (SprintWindows.cs); views join this table and never repeat the rule. ended is null while the sprint
-- is not completed: its window then runs to the as-of time.
CREATE TABLE sprint_window (
    sprint_id TEXT PRIMARY KEY,
    started   TEXT NOT NULL,
    ended     TEXT NULL
) STRICT;
