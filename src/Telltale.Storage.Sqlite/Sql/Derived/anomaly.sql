-- One row per issue and kind of changelog anomaly. message is one of a fixed set of texts
-- (ChangelogAnomalyKind): it never holds a name, a summary or a value from the changelog.
CREATE TABLE anomaly (
    issue_id TEXT NOT NULL,
    message  TEXT NOT NULL,
    PRIMARY KEY (issue_id, message)
) STRICT;
