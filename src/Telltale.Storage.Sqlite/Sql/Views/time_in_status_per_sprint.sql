-- Per sprint and status: how many issues spent time in the status during the sprint, and the median
-- business days they spent. A status no issue spent business time in has no row. Order by board_position for board order.
-- Statuses with the done role are left out: an issue in one is not waiting on the team. Their time
-- stays in sprint_status_time and status_interval.
CREATE VIEW time_in_status_per_sprint AS
WITH ranked AS (
    SELECT t.sprint_id,
           t.status_id,
           t.business_days,
           ROW_NUMBER() OVER (PARTITION BY t.sprint_id, t.status_id ORDER BY t.business_days) AS rn,
           COUNT(*)     OVER (PARTITION BY t.sprint_id, t.status_id)                          AS n
    FROM sprint_status_time t
    -- An issue counts toward a status only if it spent business time there (owner decision D21): one that
    -- was in the status only over a weekend or a holiday is in neither the count nor the median.
    WHERE t.business_days > 0
)
SELECT r.sprint_id,
       r.status_id,
       COALESCE(s.name, r.status_id) AS status_name,
       b.position                    AS board_position,
       r.n                           AS issue_count,
       -- The middle value, or the mean of the two middle values (integer division picks the rows).
       AVG(CASE WHEN r.rn IN ((r.n + 1) / 2, (r.n + 2) / 2) THEN r.business_days END) AS median_business_days
FROM ranked r
JOIN board_status b ON b.status_id = r.status_id
LEFT JOIN status s ON s.status_id = r.status_id
WHERE b.is_done = 0
GROUP BY r.sprint_id, r.status_id;
