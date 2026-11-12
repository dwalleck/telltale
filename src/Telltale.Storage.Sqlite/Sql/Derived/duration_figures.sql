-- Not a metric view: it applies no minimum group size, so only the metric views read it, and each withholds
-- what the rule says. One row per group of duration_sample (scope, measure, sprint, group_key) with the two
-- figures every table of the report shows, each picked here and nowhere else.
-- Median: the middle value, or the mean of the two middle values. P85: nearest rank, the smallest value with
-- at least 85% of the values at or below it. Both are picked by integer rank: (n + 1) / 2 and (n + 2) / 2 are
-- the same row for an odd n and the two middle rows for an even n; (85 * n + 99) / 100 is ceil(0.85 * n)
-- without floating point. Each measure is ranked on its own, since business and calendar days can order
-- issues differently. sample_count is the number of durations in the group.
CREATE VIEW duration_figures AS
WITH ranked AS (
    SELECT s.scope,
           s.measure,
           s.sprint_id,
           s.group_key,
           s.days,
           ROW_NUMBER() OVER (PARTITION BY s.scope, s.measure, s.sprint_id, s.group_key ORDER BY s.days) AS rn,
           COUNT(*)     OVER (PARTITION BY s.scope, s.measure, s.sprint_id, s.group_key)                 AS n
    FROM duration_sample s
)
SELECT r.scope,
       r.measure,
       r.sprint_id,
       r.group_key,
       r.n AS sample_count,
       AVG(CASE WHEN r.rn IN ((r.n + 1) / 2, (r.n + 2) / 2) THEN r.days END) AS median_days,
       MAX(CASE WHEN r.rn = (85 * r.n + 99) / 100 THEN r.days END)           AS p85_days
FROM ranked r
GROUP BY r.scope, r.measure, r.sprint_id, r.group_key;
