-- cycle_end is the issue's last move into a done status, null when it is not done now. delivered says
-- whether that was a delivery: an abandoned issue has a cycle_end and delivered = 0. Filter on delivered,
-- never on cycle_end. delivery_sprint_id is the sprint whose window contains a delivery: null when it
-- fell between sprints, and for every issue that is not delivered.
CREATE TABLE issue_cycle (
    issue_id           TEXT PRIMARY KEY,
    cycle_end          TEXT NULL,
    delivered          INTEGER NOT NULL,
    delivery_sprint_id TEXT NULL
) STRICT;
