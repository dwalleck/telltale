-- Cycle time of the issues delivered in each sprint, by point bucket. One row per sprint and bucket that has
-- at least one cycle time; point_bucket is null for unestimated issues. issue_count is the number of cycle
-- times behind the figures: a delivered issue that never entered a work status has none and is not in it.
--
-- The median and the P85 are duration_figures' (how each is picked is said there), for business and for
-- calendar days, which are ranked separately.
--
-- Minimum group size (GLOSSARY, ADR-0003): the four figures are null and suppressed is 1 when the sprint has
-- fewer than five distinct assignees (sprint_team) or the bucket has fewer than three behind it. A bucket
-- short of three keeps its issue_count (owner decision D5), unless the sprint's delivered count is withheld
-- because fewer than three assignees are among its delivered issues: then every bucket's issue_count is
-- null too, since the counts add up to the withheld count (owner decision D53). A sprint short of five has no bucket rows at
-- all: it has exactly one row, with no bucket, no count (D41: the counts of a small sprint are its people's
-- throughput, and summed they give back the delivered count that is withheld) and suppressed = 1, whether
-- or not anything was delivered in it (D45: which buckets have rows would say what its few people worked
-- on). In that row a null point_bucket does not mean unestimated. An issue counts toward its assignee at
-- the time of the sync, and an unassigned issue toward nobody.
CREATE VIEW cycle_time_per_sprint_and_bucket AS
WITH bucket AS (
    SELECT c.delivery_sprint_id AS sprint_id,
           c.point_bucket,
           COUNT(*) AS issue_count,
           COUNT(DISTINCT i.assignee_hash) AS assignees
    FROM issue_cycle c
    LEFT JOIN issue i ON i.issue_id = c.issue_id
    WHERE c.delivered = 1 AND c.business_days IS NOT NULL
    GROUP BY c.delivery_sprint_id, c.point_bucket
),
shown AS (
    SELECT b.sprint_id,
           b.point_bucket,
           -- Owner decision D53: where the sprint's delivered count is withheld (delivered_per_sprint), the
           -- bucket counts add up to it, so they are withheld with it. The rows stay.
           CASE WHEN d.suppressed = 0 THEN b.issue_count END AS issue_count,
           CASE WHEN b.assignees >= (SELECT assignees_behind_a_figure FROM minimum_group_size) THEN 0 ELSE 1 END AS suppressed
    FROM bucket b
    -- An inner join: an issue delivered between sprints has no sprint, so it is in no row.
    JOIN sprint_team t ON t.sprint_id = b.sprint_id
    JOIN delivered_per_sprint d ON d.sprint_id = b.sprint_id
    WHERE t.suppressed = 0
)
SELECT s.sprint_id,
       s.point_bucket,
       s.issue_count,
       CASE WHEN s.suppressed = 0 THEN business.median_days END AS median_business_days,
       CASE WHEN s.suppressed = 0 THEN business.p85_days END    AS p85_business_days,
       CASE WHEN s.suppressed = 0 THEN calendar.median_days END AS median_calendar_days,
       CASE WHEN s.suppressed = 0 THEN calendar.p85_days END    AS p85_calendar_days,
       s.suppressed
FROM shown s
JOIN duration_figures business ON business.scope = 'sprint' AND business.measure = 'cycle business' AND business.sprint_id = s.sprint_id AND business.bucket IS CAST(s.point_bucket AS REAL)
JOIN duration_figures calendar ON calendar.scope = 'sprint' AND calendar.measure = 'cycle calendar' AND calendar.sprint_id = s.sprint_id AND calendar.bucket IS CAST(s.point_bucket AS REAL)
UNION ALL
SELECT t.sprint_id, NULL, NULL, NULL, NULL, NULL, NULL, 1
FROM sprint_team t
WHERE t.suppressed = 1;
