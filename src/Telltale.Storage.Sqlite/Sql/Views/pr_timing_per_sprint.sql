-- Pull request waits per sprint: one row per sprint that counts and measure (pr_measure), when a pull request
-- host is configured; no rows otherwise. The population is every pull request of the configured repositories,
-- linked to a Jira issue or not, whoever opened it. A wait runs from the pull request's first opening to the
-- event that ended it and is counted once, in the sprint whose window holds that event, at its full length.
-- sample_count is the number of pull requests with such a wait in the sprint: 0 when there is none, and then
-- there is no figure. median_elapsed_days is the median of the waits in elapsed days (the wait divided by 24
-- hours). measure_position is the order a report lists the measures in.
--
-- Minimum group size (GLOSSARY, ADR-0003, ADR-0008): for pull request figures both checks count distinct
-- pull request authors, as salted hashes, and Jira assignees play no part. The team check: a sprint in which
-- the pull requests with a wait counted in it have fewer distinct authors than minimum_group_size.team has
-- sample_count null and suppressed = 1 in every row. The check of a figure: a row with fewer distinct authors
-- behind it than minimum_group_size.assignees_behind_a_figure keeps its sample_count and has a null figure
-- and suppressed = 1. A row with no wait has nothing to withhold. A pull request whose author the host does
-- not name counts toward nobody. A number of zero turns its check off.
CREATE VIEW pr_timing_per_sprint AS
WITH sprint_contributors AS (
    SELECT w.sprint_id,
           COUNT(DISTINCT p.author_hash) AS contributor_count
    FROM sprint_window w
    LEFT JOIN pr_wait p ON p.sprint_id = w.sprint_id
    GROUP BY w.sprint_id
),
checked AS (
    SELECT c.sprint_id,
           m.measure,
           m.position,
           CASE WHEN c.contributor_count >= g.team THEN 0 ELSE 1 END AS team_suppressed,
           CASE WHEN f.sample_count IS NULL OR f.contributor_count >= g.assignees_behind_a_figure THEN 0 ELSE 1 END AS figure_suppressed,
           COALESCE(f.sample_count, 0) AS sample_count,
           f.median_elapsed_days
    FROM sprint_contributors c
    CROSS JOIN pr_measure m
    CROSS JOIN minimum_group_size g
    LEFT JOIN pr_wait_figures f ON f.sprint_id = c.sprint_id AND f.measure = m.measure
)
SELECT k.sprint_id,
       k.measure,
       k.position AS measure_position,
       CASE WHEN k.team_suppressed = 0 THEN k.sample_count END AS sample_count,
       CASE WHEN k.team_suppressed = 0 AND k.figure_suppressed = 0 THEN k.median_elapsed_days END AS median_elapsed_days,
       CASE WHEN k.team_suppressed = 0 AND k.figure_suppressed = 0 THEN 0 ELSE 1 END AS suppressed
FROM checked k;
