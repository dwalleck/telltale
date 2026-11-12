-- cycle_end is the delivery moment; delivery_sprint_id is null when it fell between sprints.
-- cycle_start, calendar_days and business_days are null together: the issue has no cycle time (it is not
-- delivered, or it never entered a work status). point_bucket is null for an unestimated issue.
-- reopen_count is the number of moves out of a done status, for every issue, delivered or not.
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
