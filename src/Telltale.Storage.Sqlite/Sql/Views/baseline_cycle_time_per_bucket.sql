-- The baseline's cycle time by point bucket (GLOSSARY: Baseline): the median and P85 of the cycle times of all
-- issues delivered in the baseline sprints, pooled together. One row per bucket that has at least one such
-- cycle time; point_bucket is null for unestimated issues. No row when no baseline is configured. The
-- baseline sprints are the rows of baseline_sprint. The median and the P85 are duration_figures', taken of
-- its 'baseline' scope.
--
-- Minimum group size (GLOSSARY, ADR-0003; owner decisions D44, D50, D56). Two rules, neither written here:
-- 1. The whole baseline: while a baseline sprint has its delivered count withheld (baseline_sprint.suppressed:
--    fewer distinct assignees than a team, or fewer than a figure needs among its delivered issues;
--    minimum_group_size's two numbers, the configured ones, five and three by default), this view has no
--    rows at all. Rows, even with withheld figures, would say which buckets that sprint has issues in.
-- 2. One row: with every baseline sprint's delivered count shown, each has its bucket rows in
--    cycle_time_per_sprint_and_bucket, and a pooled figure is withheld (the four figures null, suppressed 1)
--    when any of them has the bucket suppressed there, that is, short of the assignees a figure needs. Otherwise the pooled
--    figure and the shown ones would give the withheld one away: with three shown cycle times and one
--    withheld, the pooled P85 is the withheld cycle time itself. A baseline sprint with no issue in the bucket
--    does not suppress it.
-- So a row that is shown is pooled from buckets with the assignees a figure needs in sprints that pass both per-sprint checks.
-- There is no issue count: the pooled count less the sprints' shown counts would be a withheld count.
CREATE VIEW baseline_cycle_time_per_bucket AS
-- One row per bucket: 1 when any baseline sprint's own view has the bucket suppressed (rule 2).
WITH withheld AS (
    SELECT v.point_bucket, MAX(v.suppressed) AS suppressed
    FROM cycle_time_per_sprint_and_bucket v
    JOIN baseline_sprint b ON b.sprint_id = v.sprint_id
    GROUP BY v.point_bucket
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
