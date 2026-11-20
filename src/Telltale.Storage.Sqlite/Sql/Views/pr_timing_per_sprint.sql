-- Pull request waits per sprint: one row per sprint that counts and measure (pr_measure), when a pull request
-- host is configured; no rows otherwise. The population is every pull request of the configured repositories,
-- linked to a Jira issue or not, whoever opened it. A wait runs from the pull request's first opening to the
-- event that ended it and is counted once, in the sprint whose window holds that event, at its full length.
-- sample_count is the number of pull requests with such a wait in the sprint: 0 when there is none, and then
-- there is no figure (the four figures are null, never zero). median_elapsed_days and p85_elapsed_days are the
-- median and the P85 of the waits in elapsed days (the wait divided by 24 hours); median_business_days and
-- p85_business_days are the same of the waits in business days (weekends and company holidays left out, in
-- the team's time zone). The median is the middle wait, or the mean of the two middle ones; the P85 is by
-- nearest rank. Each clock is ranked on its own, from the unrounded waits, so its figures need not come from
-- the same pull requests as the other's. measure_position is the order a report lists the measures in.
--
-- There is one more row per measure, with no sprint (sprint_id is null): the waits counted in no sprint,
-- because the event that ended them fell between sprint windows or in more than one. They are kept there and
-- not guessed into a sprint. Its columns mean the same, over everything collected, and its count is 0 when
-- every wait has a sprint. Every wait is in exactly one row of its measure. Read one sprint with
-- sprint_id = '...', or leave these rows out with sprint_id IS NOT NULL.
--
-- Pull request figures are not subject to the minimum group size (owner decision D114): this view applies
-- neither check, whatever minimum_group_size says, and has no suppressed flag. It is not grouped by person
-- and reads no person: the derived rows it is built from carry no author.
CREATE VIEW pr_timing_per_sprint AS
WITH counted_in AS (
    SELECT s.sprint_id FROM sprint_window s
    UNION ALL
    SELECT CAST(NULL AS TEXT)
)
SELECT w.sprint_id,
       m.measure,
       m.position AS measure_position,
       COALESCE(f.sample_count, 0) AS sample_count,
       f.median_elapsed_days,
       f.p85_elapsed_days,
       f.median_business_days,
       f.p85_business_days
FROM counted_in w
CROSS JOIN pr_measure m
LEFT JOIN pr_wait_figures f ON f.sprint_id IS w.sprint_id AND f.measure = m.measure;
