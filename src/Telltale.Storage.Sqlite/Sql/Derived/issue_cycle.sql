-- cycle_end is the issue's last move into a done status, null when it is not done now. delivered says
-- whether that was a delivery: an abandoned issue has a cycle_end and delivered = 0. Filter on delivered,
-- never on cycle_end. delivery_sprint_id is the sprint whose window contains a delivery: null when it
-- fell between sprints, and for every issue that is not delivered.
-- cycle_start, calendar_days and business_days are null together: the issue has no cycle time (it is not
-- delivered, or it never entered a work status). point_bucket is null for an unestimated issue.
-- reopen_count is the number of transitions out of a done status, for every issue, delivered or not.
CREATE TABLE issue_cycle (
    issue_id           TEXT PRIMARY KEY,
    cycle_start        TEXT NULL,
    cycle_end          TEXT NULL,
    calendar_days      REAL NULL,
    business_days      REAL NULL,
    point_bucket       REAL NULL,
    delivered          INTEGER NOT NULL,
    delivery_sprint_id TEXT NULL,
    reopen_count       INTEGER NOT NULL
) STRICT;
