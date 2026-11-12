-- cycle_end is the delivery moment; delivery_sprint_id is null when it fell between sprints.
-- An abandoned issue has a cycle_end, delivered = 0 and no delivery sprint.
CREATE TABLE issue_cycle (
    issue_id           TEXT PRIMARY KEY,
    cycle_end          TEXT NULL,
    delivered          INTEGER NOT NULL,
    delivery_sprint_id TEXT NULL
) STRICT;
