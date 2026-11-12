-- Time in status: how long an issue spent in a status during a sprint, counting only time inside the
-- sprint window and while the issue was a member of the sprint. Every status is here, done ones too.
-- calendar_seconds is clock time; business_seconds leaves out weekends and holidays; business_days
-- counts each business day by the share of it that was spent, so a 23- or 25-hour day is still one day.
CREATE TABLE sprint_status_time (
    issue_id         TEXT NOT NULL,
    sprint_id        TEXT NOT NULL,
    status_id        TEXT NOT NULL,
    calendar_seconds REAL NOT NULL,
    business_seconds REAL NOT NULL,
    business_days    REAL NOT NULL,
    PRIMARY KEY (issue_id, sprint_id, status_id)
) STRICT;
