-- Time in status: how long an issue spent in a status during a sprint, counting only time inside the
-- sprint window and while the issue was a member of the sprint. Every status is here, done ones too.
-- calendar_seconds is clock time; business_seconds leaves out weekends and holidays; business_days
-- counts each business day by the share of it that was spent, so a 23- or 25-hour day is still one day.
-- Those three are the time as recorded, whatever the treatment of brief visits (GLOSSARY: Treatment): the
-- team of a sprint (sprint_team) and the rows the brief visit count is measured against
-- (time_in_status_issue_recorded) are taken from them. measured_business_days is what the time in status
-- figures are taken from (time_in_status_issue): business_days, less the brief visits to listed statuses
-- when the treatment for time in status leaves them out (owner decision D84). It equals business_days when
-- nothing is left out, and is 0 when nothing remains.
CREATE TABLE sprint_status_time (
    issue_id         TEXT NOT NULL,
    sprint_id        TEXT NOT NULL,
    status_id        TEXT NOT NULL,
    calendar_seconds REAL NOT NULL,
    business_seconds REAL NOT NULL,
    business_days    REAL NOT NULL,
    measured_business_days REAL NOT NULL,
    PRIMARY KEY (issue_id, sprint_id, status_id)
) STRICT;
