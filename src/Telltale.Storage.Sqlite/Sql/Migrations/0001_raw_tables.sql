-- Raw tables: selected fields only (ADR-0002). No summary, description, comment, name, email or account ID.
CREATE TABLE issue (
    issue_id      TEXT PRIMARY KEY,
    issue_key     TEXT NOT NULL,
    issue_type_id TEXT NOT NULL,
    created       TEXT NOT NULL,
    status_id     TEXT NOT NULL,
    resolution_id TEXT NULL,
    story_points  REAL NULL,
    assignee_hash TEXT NULL,
    updated       TEXT NOT NULL
) STRICT;

-- One row per changed field of a changelog entry. field is 'status' (values are status IDs).
CREATE TABLE change (
    history_id TEXT NOT NULL,
    field      TEXT NOT NULL,
    issue_id   TEXT NOT NULL,
    at         TEXT NOT NULL,
    from_value TEXT NULL,
    to_value   TEXT NULL,
    PRIMARY KEY (history_id, field)
) STRICT;

CREATE INDEX change_issue ON change (issue_id);

CREATE TABLE sprint (
    sprint_id   TEXT PRIMARY KEY,
    board_id    INTEGER NOT NULL,
    name        TEXT NOT NULL,
    state       TEXT NOT NULL,
    started     TEXT NULL,
    planned_end TEXT NULL,
    completed   TEXT NULL
) STRICT;

CREATE TABLE status (
    status_id TEXT PRIMARY KEY,
    name      TEXT NOT NULL,
    category  TEXT NOT NULL
) STRICT;
