-- Pull request waits per sprint: one row per sprint that counts, measure (pr_measure) and host scope, when a
-- pull request host is configured; no rows otherwise. The population is every pull request of the configured
-- repositories, linked to a Jira issue or not, whoever opened it. A wait runs from the pull request's first
-- opening to the event that ended it and is counted once, in the sprint whose window holds that event, at its
-- full length.
--
-- host_scope says whose pull requests the row is of: a configured host ('github', 'ado'), or 'combined' for
-- every configured host together. Each configured host has its rows. The 'combined' rows are there only when
-- more than one host is configured: with one host its rows are everything, and a query needs no filter. A
-- combined figure is taken of the hosts' pooled waits, by the same picking as any other figure, never of the
-- hosts' medians or P85s. host_scope_position orders the scopes: the hosts as configured, 'combined' last.
-- A pull request is one by host, repository and number: the same repository name and number on two hosts are
-- two pull requests, each in its own host's rows and both in the combined ones.
--
-- coverage says what the row's count rests on. For a host's row it is pr_host_coverage's: 'read', 'failed
-- read', 'not read' or 'reviews not read'. For a combined row it is 'complete' when every configured host is
-- 'read' for the measure, and 'partial' otherwise: the row then holds only what the store has, which leaves
-- out whatever the hosts that are not 'read' would add.
--
-- sample_count is the number of pull requests with such a wait in the sprint. Under coverage 'read' or
-- 'complete' it is 0 when there is none. Under any other coverage it is null when the store holds no such
-- wait: what a host that was not read has is not known, and is not zero. Without a count above 0 there is no
-- figure (the four figures are null, never zero). median_elapsed_days and p85_elapsed_days are the
-- median and the P85 of the waits in elapsed days (the wait divided by 24 hours); median_business_days and
-- p85_business_days are the same of the waits in business days (weekends and company holidays left out, in
-- the team's time zone). The median is the middle wait, or the mean of the two middle ones; the P85 is by
-- nearest rank. Each clock is ranked on its own, from the unrounded waits, so its figures need not come from
-- the same pull requests as the other's. measure_position is the order a report lists the measures in.
--
-- There is one more row per measure and host scope, with no sprint (sprint_id is null): the waits counted in
-- no sprint, because the event that ended them fell between sprint windows or in more than one. They are kept
-- there and not guessed into a sprint. Its columns mean the same, over everything collected. Every wait is in
-- exactly one row of its measure and host scope. Read one sprint with sprint_id = '...', or leave these rows
-- out with sprint_id IS NOT NULL.
--
-- Pull request figures are not subject to the minimum group size (owner decision D114): this view applies
-- neither check, whatever minimum_group_size says, and has no suppressed flag. It is not grouped by person
-- and reads no person: the derived rows it is built from carry no author.
CREATE VIEW pr_timing_per_sprint AS
WITH counted_in AS (
    SELECT s.sprint_id FROM sprint_window s
    UNION ALL
    SELECT CAST(NULL AS TEXT)
),
scoped AS (
    SELECT c.host AS host_scope, c.position, c.measure, c.coverage, c.coverage = 'read' AS covered
    FROM pr_host_coverage c
    UNION ALL
    SELECT 'combined', MAX(c.position) + 1, c.measure, CASE WHEN MIN(c.coverage = 'read') = 1 THEN 'complete' ELSE 'partial' END, MIN(c.coverage = 'read')
    FROM pr_host_coverage c
    GROUP BY c.measure
    HAVING COUNT(*) > 1
)
SELECT w.sprint_id,
       m.measure,
       m.position AS measure_position,
       s.host_scope,
       s.position AS host_scope_position,
       s.coverage,
       CASE WHEN s.covered = 1 THEN COALESCE(f.sample_count, 0) ELSE f.sample_count END AS sample_count,
       f.median_elapsed_days,
       f.p85_elapsed_days,
       f.median_business_days,
       f.p85_business_days
FROM counted_in w
CROSS JOIN pr_measure m
JOIN scoped s ON s.measure = m.measure
LEFT JOIN pr_wait_figures f ON f.sprint_id IS w.sprint_id AND f.measure = m.measure AND f.host_scope = s.host_scope;
