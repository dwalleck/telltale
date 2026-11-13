-- cycle_end is the issue's last move into a done status. It is null when the issue is not done now, and
-- also when it is in a done status but its changelog ends in a status that is not done (owner decision D8)
-- or holds no move into done at all (D9): such an issue is not delivered. delivered says
-- whether that was a delivery: an abandoned issue has a cycle_end and delivered = 0. Filter on delivered,
-- never on cycle_end. delivery_sprint_id is the sprint whose window contains a delivery: null when it
-- fell between sprints, and for every issue that is not delivered.
-- cycle_start, calendar_days and business_days are null together: the issue has no cycle time (it is not
-- delivered, or it never entered a work status). point_bucket is set for delivered issues only: null for
-- one that is unestimated, and for every issue that is not delivered, whatever its estimate.
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
