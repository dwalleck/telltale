-- Per sprint and status: how many issues spent time in the status during the sprint, and the median
-- business days they spent. A status no issue spent business time in has no row. Order by board_position for board order.
-- Statuses with the done role are left out: an issue in one is not waiting on the team. Their time
-- stays in sprint_status_time and status_interval.
-- Minimum group size (GLOSSARY, ADR-0003): the median is null and suppressed is 1 when the sprint has fewer
-- than five distinct assignees (sprint_team) or fewer than three are behind the row's issues. A row short
-- of three keeps its issue_count (owner decision D5); in a sprint short of five issue_count is null too (D41).
-- An unassigned issue counts toward nobody.
CREATE VIEW time_in_status_per_sprint AS
WITH ranked AS (
    SELECT t.sprint_id,
           t.status_id,
           t.business_days,
           i.assignee_hash,
           ROW_NUMBER() OVER (PARTITION BY t.sprint_id, t.status_id ORDER BY t.business_days) AS rn,
           COUNT(*)     OVER (PARTITION BY t.sprint_id, t.status_id)                          AS n
    FROM sprint_status_time t
    LEFT JOIN issue i ON i.issue_id = t.issue_id
    -- An issue counts toward a status only if it spent business time there (owner decision D21): one that
    -- was in the status only over a weekend or a holiday is in neither the count nor the median.
    WHERE t.business_days > 0
),
status_time AS (
    SELECT r.sprint_id,
           r.status_id,
           r.n AS issue_count,
           COUNT(DISTINCT r.assignee_hash) AS assignees,
           -- The middle value, or the mean of the two middle values (integer division picks the rows).
           AVG(CASE WHEN r.rn IN ((r.n + 1) / 2, (r.n + 2) / 2) THEN r.business_days END) AS median_business_days
    FROM ranked r
    GROUP BY r.sprint_id, r.status_id
)
SELECT st.sprint_id,
       st.status_id,
       COALESCE(s.name, st.status_id) AS status_name,
       b.position                     AS board_position,
       CASE WHEN t.suppressed = 0 THEN st.issue_count END AS issue_count,
       CASE WHEN t.suppressed = 0 AND st.assignees >= 3 THEN st.median_business_days END AS median_business_days,
       CASE WHEN t.suppressed = 0 AND st.assignees >= 3 THEN 0 ELSE 1 END                AS suppressed
FROM status_time st
JOIN sprint_team t ON t.sprint_id = st.sprint_id
JOIN board_status b ON b.status_id = st.status_id
LEFT JOIN status s ON s.status_id = st.status_id
WHERE b.is_done = 0;
