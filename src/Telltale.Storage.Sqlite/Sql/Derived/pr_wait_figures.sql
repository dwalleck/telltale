-- An internal view, read by pr_timing_per_sprint: one row per sprint and measure with at least one wait
-- counted in the sprint. The median is picked as duration_figures picks it: the middle value, or the mean of
-- the two middle values, by integer rank ((n + 1) / 2 and (n + 2) / 2).
CREATE VIEW pr_wait_figures AS
WITH ranked AS (
    SELECT p.sprint_id,
           p.measure,
           p.elapsed_days,
           ROW_NUMBER() OVER (PARTITION BY p.sprint_id, p.measure ORDER BY p.elapsed_days) AS rn,
           COUNT(*)     OVER (PARTITION BY p.sprint_id, p.measure)                         AS n
    FROM pr_wait p
    WHERE p.sprint_id IS NOT NULL
)
SELECT r.sprint_id,
       r.measure,
       r.n AS sample_count,
       AVG(CASE WHEN r.rn IN ((r.n + 1) / 2, (r.n + 2) / 2) THEN r.elapsed_days END) AS median_elapsed_days
FROM ranked r
GROUP BY r.sprint_id, r.measure;
