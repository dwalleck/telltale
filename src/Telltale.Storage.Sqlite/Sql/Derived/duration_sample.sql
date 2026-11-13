-- Not a metric view: one row per issue and duration, which only the views that apply the minimum group size
-- may read. It lists every duration a median or a P85 is taken of, so that the picking of the two is written
-- once (duration_figures) for every table of the report.
-- measure says what the duration is: 'cycle business' and 'cycle calendar' are the cycle time of an issue
-- delivered in the sprint, in business and in calendar days; 'status' is the business days an issue spent in
-- a status during the sprint (only if it spent any: owner decision D21).
-- What a duration is grouped by has a column of its own type, because one column for both would hold numbers
-- and text, and SQLite leaves the affinity of such a column of a compound SELECT unspecified: a join on it
-- could compare a number with text. bucket is the point bucket of a cycle time, a REAL (null for unestimated,
-- and for every 'status' row); status_id is the status of a time in status, a TEXT (null for every cycle row).
-- scope 'sprint' has each duration under its own sprint. scope 'baseline' has the durations of the baseline
-- sprints (baseline_sprint) once more with no sprint, pooled: an issue that was in a status during two
-- baseline sprints is there once for each.
CREATE VIEW duration_sample AS
WITH per_sprint AS (
    SELECT 'cycle business' AS measure, c.delivery_sprint_id AS sprint_id, CAST(c.point_bucket AS REAL) AS bucket, CAST(NULL AS TEXT) AS status_id, c.business_days AS days
    FROM issue_cycle c
    WHERE c.delivered = 1 AND c.business_days IS NOT NULL
    UNION ALL
    SELECT 'cycle calendar', c.delivery_sprint_id, CAST(c.point_bucket AS REAL), CAST(NULL AS TEXT), c.calendar_days
    FROM issue_cycle c
    WHERE c.delivered = 1 AND c.business_days IS NOT NULL
    UNION ALL
    SELECT 'status', t.sprint_id, CAST(NULL AS REAL), CAST(t.status_id AS TEXT), t.business_days
    FROM sprint_status_time t
    WHERE t.business_days > 0
)
SELECT 'sprint' AS scope, p.measure, p.sprint_id, p.bucket, p.status_id, p.days
FROM per_sprint p
UNION ALL
SELECT 'baseline', p.measure, NULL, p.bucket, p.status_id, p.days
FROM per_sprint p
JOIN baseline_sprint b ON b.sprint_id = p.sprint_id;
