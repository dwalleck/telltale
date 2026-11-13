-- The baseline's cycle time by point bucket (GLOSSARY: Baseline): the median and P85 of the cycle times of all
-- issues delivered in the baseline sprints, pooled together. One row per bucket that has at least one such
-- cycle time; point_bucket is null for unestimated issues. No row when no baseline is configured. The
-- baseline sprints are the rows of baseline_sprint. The median and the P85 are duration_figures', taken of
-- its 'baseline' scope.
--
-- Minimum group size (GLOSSARY, ADR-0003; owner decisions D44, D50). A pooled figure sits beside the figures
-- of the sprints it is pooled from, so it is withheld whenever one of those is: the four figures are null and
-- suppressed is 1 when cycle_time_per_sprint_and_bucket has the bucket suppressed for any baseline sprint.
-- Otherwise the pooled figure and the shown ones would give the withheld one away: with three shown cycle
-- times and one withheld, the pooled P85 is the withheld cycle time itself. The rule itself is in that view
-- and is read here, not repeated. A baseline sprint with no issue in the bucket does not suppress it.
-- A baseline sprint must itself have at least five distinct assignees (owner decision D50, which changed
-- D47): while one has not (baseline_sprint.suppressed), this view has no rows at all. Rows with withheld
-- figures would still say which buckets the small sprint had issues in, which its own report withholds.
-- So a row is withheld only for a bucket some baseline sprint's own table shows, with its count, as short of
-- three assignees; and a row that is shown is pooled from buckets of three or more in sprints of five or
-- more, so the pooled group passes both checks as well.
-- There is no issue count: the pooled count less the sprints' shown counts would be a withheld count.
CREATE VIEW baseline_cycle_time_per_bucket AS
-- One row per bucket: 1 when any baseline sprint's own view withholds the bucket.
WITH per_sprint AS (
    SELECT v.point_bucket, v.suppressed
    FROM cycle_time_per_sprint_and_bucket v
    JOIN baseline_sprint b ON b.sprint_id = v.sprint_id
    -- A baseline sprint whose delivered count is withheld has no bucket rows in that view, only its one row.
    -- Every bucket it has a cycle time in counts as suppressed (owner decision D54), read from the samples
    -- and not from a row that could be missing, so the baseline fails closed.
    JOIN sprint_team t ON t.sprint_id = v.sprint_id
    WHERE t.delivered_suppressed = 0
    UNION ALL
    SELECT f.bucket, 1
    FROM duration_figures f
    JOIN baseline_sprint b ON b.sprint_id = f.sprint_id
    JOIN sprint_team t ON t.sprint_id = f.sprint_id
    WHERE f.scope = 'sprint' AND f.measure = 'cycle business' AND t.delivered_suppressed <> 0
),
withheld AS (
    SELECT p.point_bucket, MAX(p.suppressed) AS suppressed
    FROM per_sprint p
    GROUP BY p.point_bucket
)
SELECT business.bucket AS point_bucket,
       CASE WHEN w.suppressed = 0 THEN business.median_days END AS median_business_days,
       CASE WHEN w.suppressed = 0 THEN business.p85_days END    AS p85_business_days,
       CASE WHEN w.suppressed = 0 THEN calendar.median_days END AS median_calendar_days,
       CASE WHEN w.suppressed = 0 THEN calendar.p85_days END    AS p85_calendar_days,
       CASE WHEN w.suppressed = 0 THEN 0 ELSE 1 END             AS suppressed
FROM duration_figures business
JOIN duration_figures calendar ON calendar.scope = 'baseline' AND calendar.measure = 'cycle calendar' AND calendar.bucket IS business.bucket
-- A left join: a bucket the per-sprint view has no row for (it should have one for every pooled issue) reads
-- as withheld, never as shown.
LEFT JOIN withheld w ON CAST(w.point_bucket AS REAL) IS business.bucket
WHERE business.scope = 'baseline' AND business.measure = 'cycle business'
  AND NOT EXISTS (SELECT 1 FROM baseline_sprint WHERE suppressed <> 0);
