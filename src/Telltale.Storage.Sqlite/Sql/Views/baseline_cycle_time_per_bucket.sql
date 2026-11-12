-- The baseline's cycle time by point bucket (GLOSSARY: Baseline): the median and P85 of the cycle times of all
-- issues delivered in the baseline sprints, pooled together. One row per bucket that has at least one such
-- cycle time; point_bucket is null for unestimated issues. No row when no baseline is configured. Median and
-- P85 are picked as in cycle_time_per_sprint_and_bucket.
--
-- Minimum group size (GLOSSARY, ADR-0003; owner decision D44). A pooled figure sits beside the figures of the
-- sprints it is pooled from, so it is withheld whenever one of those is: the four figures are null and
-- suppressed is 1 when any baseline sprint with an issue in the bucket has that bucket suppressed in its own
-- view, that is, the sprint has fewer than five distinct assignees (sprint_team) or its issues in the bucket
-- have fewer than three. Otherwise the pooled figure and the shown ones would give the withheld one away: with
-- three shown cycle times and one withheld, the pooled P85 is the withheld cycle time itself. A baseline
-- sprint with no issue in the bucket does not suppress it. A row that passes is pooled only from cells of at
-- least three assignees in sprints of at least five, so the pooled group passes both checks as well.
-- There is no issue count: the pooled count less the sprints' shown counts would be a withheld count.
CREATE VIEW baseline_cycle_time_per_bucket AS
WITH pooled AS (
    SELECT c.delivery_sprint_id AS sprint_id,
           c.point_bucket,
           c.business_days,
           c.calendar_days,
           i.assignee_hash,
           ROW_NUMBER() OVER (PARTITION BY c.point_bucket ORDER BY c.business_days) AS business_rank,
           ROW_NUMBER() OVER (PARTITION BY c.point_bucket ORDER BY c.calendar_days) AS calendar_rank,
           COUNT(*)     OVER (PARTITION BY c.point_bucket)                          AS n
    FROM issue_cycle c
    JOIN baseline_sprint b ON b.sprint_id = c.delivery_sprint_id
    LEFT JOIN issue i ON i.issue_id = c.issue_id
    WHERE c.delivered = 1 AND c.business_days IS NOT NULL
),
-- One row per baseline sprint and bucket: whether that sprint's own view shows the bucket.
cell AS (
    SELECT p.point_bucket,
           CASE WHEN MAX(t.suppressed) = 0 AND COUNT(DISTINCT p.assignee_hash) >= 3 THEN 0 ELSE 1 END AS suppressed
    FROM pooled p
    LEFT JOIN sprint_team t ON t.sprint_id = p.sprint_id
    GROUP BY p.sprint_id, p.point_bucket
),
bucket AS (
    SELECT p.point_bucket,
           (SELECT MAX(c.suppressed) FROM cell c WHERE c.point_bucket IS p.point_bucket) AS suppressed,
           AVG(CASE WHEN p.business_rank IN ((p.n + 1) / 2, (p.n + 2) / 2) THEN p.business_days END) AS median_business_days,
           MAX(CASE WHEN p.business_rank = (85 * p.n + 99) / 100 THEN p.business_days END)           AS p85_business_days,
           AVG(CASE WHEN p.calendar_rank IN ((p.n + 1) / 2, (p.n + 2) / 2) THEN p.calendar_days END) AS median_calendar_days,
           MAX(CASE WHEN p.calendar_rank = (85 * p.n + 99) / 100 THEN p.calendar_days END)           AS p85_calendar_days
    FROM pooled p
    GROUP BY p.point_bucket
)
SELECT b.point_bucket,
       CASE WHEN b.suppressed = 0 THEN b.median_business_days END AS median_business_days,
       CASE WHEN b.suppressed = 0 THEN b.p85_business_days END    AS p85_business_days,
       CASE WHEN b.suppressed = 0 THEN b.median_calendar_days END AS median_calendar_days,
       CASE WHEN b.suppressed = 0 THEN b.p85_calendar_days END    AS p85_calendar_days,
       CASE WHEN b.suppressed = 0 THEN 0 ELSE 1 END               AS suppressed
FROM bucket b;
