-- The baseline's time in status (GLOSSARY: Baseline): per status, the median business days issues spent in it
-- during the baseline sprints, pooled together. An issue that was in a status during two baseline sprints
-- counts once for each, with its time in that sprint. One row per status some issue spent business time in
-- during a baseline sprint; done-role statuses are left out, as in time_in_status_per_sprint. No row when no
-- baseline is configured. Order by board_position for board order.
--
-- Minimum group size (GLOSSARY, ADR-0003; owner decisions D44, D50), as in baseline_cycle_time_per_bucket: no
-- row at all while a baseline sprint has fewer than five distinct assignees (baseline_sprint.suppressed); the
-- median is null and suppressed is 1 when time_in_status_per_sprint has the status suppressed for any
-- baseline sprint. The rule is in that view and is read here. A baseline sprint with no issue in the status
-- does not suppress it. There is no issue count.
CREATE VIEW baseline_time_in_status AS
-- One row per status: 1 when any baseline sprint's own view withholds the status.
WITH withheld AS (
    SELECT v.status_id, MAX(v.suppressed) AS suppressed
    FROM time_in_status_per_sprint v
    JOIN baseline_sprint b ON b.sprint_id = v.sprint_id
    GROUP BY v.status_id
)
SELECT f.status_id,
       COALESCE(s.name, f.status_id)  AS status_name,
       b.position                     AS board_position,
       CASE WHEN w.suppressed = 0 THEN f.median_days END AS median_business_days,
       CASE WHEN w.suppressed = 0 THEN 0 ELSE 1 END      AS suppressed
FROM duration_figures f
LEFT JOIN withheld w ON CAST(w.status_id AS TEXT) = CAST(f.status_id AS TEXT)
JOIN board_status b ON CAST(b.status_id AS TEXT) = CAST(f.status_id AS TEXT)
LEFT JOIN status s ON CAST(s.status_id AS TEXT) = CAST(f.status_id AS TEXT)
WHERE f.scope = 'baseline' AND f.measure = 'status'
  AND b.is_done = 0
  AND NOT EXISTS (SELECT 1 FROM baseline_sprint WHERE suppressed <> 0);
