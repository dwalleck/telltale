-- An internal view, read by pr_timing_per_sprint: one row per sprint, measure and host scope with at least one
-- wait counted in the sprint, and one per measure and host scope with at least one wait counted in no sprint
-- (sprint_id null). host_scope is a host (pr_wait.host), for that host's waits alone, or 'combined', for the
-- waits of every host pooled (duration_sample): there is a 'combined' row wherever a host has one.
-- The median and the P85 are duration_figures' (how each is picked is said there, and nowhere else), for
-- elapsed and for business days, which are ranked separately: the two clocks can order the same pull requests
-- differently, so a figure in one need not come from the pull request behind the figure in the other. Every
-- wait has both durations, so sample_count is the same for both clocks.
CREATE VIEW pr_wait_figures AS
SELECT elapsed.sprint_id,
       elapsed.pr_measure AS measure,
       elapsed.host_scope,
       elapsed.sample_count,
       elapsed.median_days  AS median_elapsed_days,
       elapsed.p85_days     AS p85_elapsed_days,
       business.median_days AS median_business_days,
       business.p85_days    AS p85_business_days
FROM duration_figures elapsed
JOIN duration_figures business ON business.scope = 'sprint' AND business.measure = 'pr business' AND business.sprint_id IS elapsed.sprint_id AND business.pr_measure = elapsed.pr_measure AND business.host_scope = elapsed.host_scope
WHERE elapsed.scope = 'sprint' AND elapsed.measure = 'pr elapsed';
