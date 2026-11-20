-- Pull request waits per sprint: one row per sprint that counts and measure (pr_measure), when a pull request
-- host is configured; no rows otherwise. The population is every pull request of the configured repositories,
-- linked to a Jira issue or not, whoever opened it. A wait runs from the pull request's first opening to the
-- event that ended it and is counted once, in the sprint whose window holds that event, at its full length.
-- sample_count is the number of pull requests with such a wait in the sprint: 0 when there is none, and then
-- there is no figure (median_elapsed_days is null). median_elapsed_days is the median of the waits in elapsed
-- days (the wait divided by 24 hours). measure_position is the order a report lists the measures in.
--
-- Pull request figures are not subject to the minimum group size (owner decision D114): this view applies
-- neither check, whatever minimum_group_size says, and has no suppressed flag. It is not grouped by person
-- and reads no person: the derived rows it is built from carry no author.
CREATE VIEW pr_timing_per_sprint AS
SELECT w.sprint_id,
       m.measure,
       m.position AS measure_position,
       COALESCE(f.sample_count, 0) AS sample_count,
       f.median_elapsed_days
FROM sprint_window w
CROSS JOIN pr_measure m
LEFT JOIN pr_wait_figures f ON f.sprint_id = w.sprint_id AND f.measure = m.measure;
