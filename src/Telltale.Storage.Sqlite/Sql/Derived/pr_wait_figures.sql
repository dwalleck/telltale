-- Not a metric view: it applies no minimum group size, so only pr_timing_per_sprint reads it, and withholds
-- what the rule says. One row per sprint and measure with at least one wait counted in the sprint.
-- The median is picked as duration_figures picks it: the middle value, or the mean of the two middle values,
-- by integer rank ((n + 1) / 2 and (n + 2) / 2). contributor_count is the number of distinct pull request
-- authors behind the figure; a pull request whose author is unknown counts toward nobody.
CREATE VIEW pr_wait_figures AS
WITH ranked AS (
    SELECT p.sprint_id,
           p.measure,
           p.author_hash,
           p.elapsed_days,
           ROW_NUMBER() OVER (PARTITION BY p.sprint_id, p.measure ORDER BY p.elapsed_days) AS rn,
           COUNT(*)     OVER (PARTITION BY p.sprint_id, p.measure)                         AS n
    FROM pr_wait p
    WHERE p.sprint_id IS NOT NULL
)
SELECT r.sprint_id,
       r.measure,
       r.n AS sample_count,
       COUNT(DISTINCT r.author_hash) AS contributor_count,
       AVG(CASE WHEN r.rn IN ((r.n + 1) / 2, (r.n + 2) / 2) THEN r.elapsed_days END) AS median_elapsed_days
FROM ranked r
GROUP BY r.sprint_id, r.measure;
