-- The baseline's cycle time by point bucket (GLOSSARY: Baseline): the median and P85 of the cycle times of all
-- issues delivered in the baseline sprints, pooled together. One row per bucket that has at least one such
-- cycle time; point_bucket is null for unestimated issues. No row when no baseline is configured. Median and
-- P85 are picked as in cycle_time_per_sprint_and_bucket.
--
-- Minimum group size (GLOSSARY, ADR-0003; owner decisions D44, D47). A pooled figure sits beside the figures
-- of the sprints it is pooled from, so it is withheld whenever one of those is: the four figures are null and
-- suppressed is 1 when cycle_time_per_sprint_and_bucket has the bucket suppressed for any baseline sprint.
-- Otherwise the pooled figure and the shown ones would give the withheld one away: with three shown cycle
-- times and one withheld, the pooled P85 is the withheld cycle time itself. The rule itself is in that view
-- and is read here, not repeated. A baseline sprint with no issue in the bucket does not suppress it.
-- Every baseline sprint has at least five distinct assignees: derive refuses a list that holds another (D47).
-- So a row is withheld only for a bucket some baseline sprint's own table shows, with its count, as short of
-- three assignees; and a row that is shown is pooled from buckets of three or more in sprints of five or
-- more, so the pooled group passes both checks as well.
-- There is no issue count: the pooled count less the sprints' shown counts would be a withheld count.
CREATE VIEW baseline_cycle_time_per_bucket AS
WITH pooled AS (
    SELECT c.point_bucket,
           c.business_days,
           c.calendar_days,
           ROW_NUMBER() OVER (PARTITION BY c.point_bucket ORDER BY c.business_days) AS business_rank,
           ROW_NUMBER() OVER (PARTITION BY c.point_bucket ORDER BY c.calendar_days) AS calendar_rank,
           COUNT(*)     OVER (PARTITION BY c.point_bucket)                          AS n
    FROM issue_cycle c
    JOIN baseline_sprint b ON b.sprint_id = c.delivery_sprint_id
    WHERE c.delivered = 1 AND c.business_days IS NOT NULL
),
-- One row per bucket: 1 when any baseline sprint's own view withholds the bucket.
withheld AS (
    SELECT v.point_bucket, MAX(v.suppressed) AS suppressed
    FROM cycle_time_per_sprint_and_bucket v
    JOIN baseline_sprint b ON b.sprint_id = v.sprint_id
    GROUP BY v.point_bucket
),
bucket AS (
    SELECT p.point_bucket,
           AVG(CASE WHEN p.business_rank IN ((p.n + 1) / 2, (p.n + 2) / 2) THEN p.business_days END) AS median_business_days,
           MAX(CASE WHEN p.business_rank = (85 * p.n + 99) / 100 THEN p.business_days END)           AS p85_business_days,
           AVG(CASE WHEN p.calendar_rank IN ((p.n + 1) / 2, (p.n + 2) / 2) THEN p.calendar_days END) AS median_calendar_days,
           MAX(CASE WHEN p.calendar_rank = (85 * p.n + 99) / 100 THEN p.calendar_days END)           AS p85_calendar_days
    FROM pooled p
    GROUP BY p.point_bucket
)
SELECT b.point_bucket,
       CASE WHEN w.suppressed = 0 THEN b.median_business_days END AS median_business_days,
       CASE WHEN w.suppressed = 0 THEN b.p85_business_days END    AS p85_business_days,
       CASE WHEN w.suppressed = 0 THEN b.median_calendar_days END AS median_calendar_days,
       CASE WHEN w.suppressed = 0 THEN b.p85_calendar_days END    AS p85_calendar_days,
       CASE WHEN w.suppressed = 0 THEN 0 ELSE 1 END               AS suppressed
FROM bucket b
-- A left join: a bucket the per-sprint view has no row for (it should have one for every pooled issue) reads
-- as withheld, never as shown.
LEFT JOIN withheld w ON w.point_bucket IS b.point_bucket;
