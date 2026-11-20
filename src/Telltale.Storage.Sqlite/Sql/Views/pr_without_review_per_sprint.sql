-- Pull requests without a first code review, per sprint: the numbers that explain how many pull requests are
-- behind the first code review figure of pr_timing_per_sprint. One row per sprint that counts and host scope,
-- when a pull request host is configured; no rows otherwise. host_scope, host_scope_position and coverage are
-- pr_timing_per_sprint's for the measure 'first_code_review' (pr_host_coverage): 'github' or 'ado' for one
-- configured host's pull requests, and 'combined' for those of every configured host together, which has
-- rows only when more than one host is configured.
--
-- A sprint is looked at as of its close, or as of the as-of time while it is still running. What happened
-- later is left out: a pull request reviewed after the close was waiting at the close and is counted so. A
-- pull request is looked at when it was opened before that moment and was still open then or closed during
-- the sprint. One that had its first code review by then is in no column. Every other one is in exactly one of:
--   waiting                open at that moment, with no first code review yet
--   closed_without_review  closed during the sprint, merged or not, with none before it closed
--   unclassifiable         an event that could have been its first code review cannot be classified, so it
--                          has no first code review that can be relied on
--   history_not_read       its events were not read at the last collection (the host refused them, or the
--                          Ops group's members could not be read) and the events stored before give no
--                          first code review by that moment, so whether it was reviewed is not known.
--                          This is not a history that was read and found empty, which counts as no_activity.
-- The other three columns split waiting and closed_without_review together by what had been submitted before
-- the pull request closed, so they repeat those pull requests and add none: no_activity + automation_only +
-- ops_only = waiting + closed_without_review.
--   ops_only         something by the Ops group, whatever automation did besides. Null for 'ado': Azure
--                    DevOps has no Ops group set apart.
--   automation_only  something by automation and nothing by the Ops group
--   no_activity      nothing, or only the author's own
-- These are numbers of pull requests and never waits: none of them is a sample of pr_timing_per_sprint, of
-- zero days or any other length. A merge is counted there whatever is said here.
--
-- As in pr_timing_per_sprint, a count of 0 is a count only under coverage 'read' (combined: 'complete'). Under
-- any other coverage the numbers are of what the store holds, and one that would be 0 is null: not known. As
-- there, a host's row of a sprint that began before the start date its pull requests were collected under
-- (pr_host_coverage.collect_from) is 'partial' in place of 'read'. A sprint that began on or after the date
-- is 'read'. One thing that leaves open: GitHub's start date bounds the last update, so a pull request left
-- open and untouched since before the date is not in the store, and is in no sprint's waiting number.
--
-- Like the pull request figures, these counts are not subject to the minimum group size (owner decision
-- D114): the view applies neither check and has no suppressed flag. It reads no person.
CREATE VIEW pr_without_review_per_sprint AS
WITH host_scope AS (
    SELECT s.sprint_id,
           c.host,
           c.position,
           CASE WHEN c.coverage = 'read' AND c.collect_from IS NOT NULL AND s.started < c.collect_from THEN 'partial' ELSE c.coverage END AS coverage
    FROM sprint_window s
    CROSS JOIN pr_host_coverage c
    WHERE c.measure = 'first_code_review'
),
scope AS (
    SELECT h.sprint_id, h.host AS host_scope, h.host, h.position, h.coverage, h.coverage = 'read' AS covered
    FROM host_scope h
    UNION ALL
    SELECT h.sprint_id, 'combined', h.host, a.position, a.coverage, a.covered
    FROM host_scope h
    JOIN (
        SELECT sprint_id, MAX(position) + 1 AS position, CASE WHEN MIN(coverage = 'read') = 1 THEN 'complete' ELSE 'partial' END AS coverage, MIN(coverage = 'read') AS covered
        FROM host_scope
        GROUP BY sprint_id
        HAVING COUNT(*) > 1
    ) a ON a.sprint_id = h.sprint_id
),
counted AS (
    SELECT c.sprint_id,
           c.host_scope,
           c.position,
           c.coverage,
           c.covered,
           COUNT(CASE WHEN p.state = 'waiting' THEN 1 END) AS waiting,
           COUNT(CASE WHEN p.state = 'closed without review' THEN 1 END) AS closed_without_review,
           COUNT(CASE WHEN p.state IN ('waiting', 'closed without review') AND p.activity = 'none' THEN 1 END) AS no_activity,
           COUNT(CASE WHEN p.state IN ('waiting', 'closed without review') AND p.activity = 'automation only' THEN 1 END) AS automation_only,
           COUNT(CASE WHEN p.state IN ('waiting', 'closed without review') AND p.activity = 'ops only' THEN 1 END) AS ops_only,
           COUNT(CASE WHEN p.state = 'unclassifiable' THEN 1 END) AS unclassifiable,
           COUNT(CASE WHEN p.state = 'history not read' THEN 1 END) AS history_not_read
    FROM scope c
    LEFT JOIN pr_without_review p ON p.sprint_id = c.sprint_id AND p.host = c.host
    GROUP BY c.sprint_id, c.host_scope, c.position, c.coverage, c.covered
)
SELECT sprint_id,
       host_scope,
       position AS host_scope_position,
       coverage,
       CASE WHEN covered = 1 THEN waiting ELSE NULLIF(waiting, 0) END AS waiting,
       CASE WHEN covered = 1 THEN closed_without_review ELSE NULLIF(closed_without_review, 0) END AS closed_without_review,
       CASE WHEN covered = 1 THEN no_activity ELSE NULLIF(no_activity, 0) END AS no_activity,
       CASE WHEN covered = 1 THEN automation_only ELSE NULLIF(automation_only, 0) END AS automation_only,
       CASE WHEN host_scope = 'ado' THEN NULL WHEN covered = 1 THEN ops_only ELSE NULLIF(ops_only, 0) END AS ops_only,
       CASE WHEN covered = 1 THEN unclassifiable ELSE NULLIF(unclassifiable, 0) END AS unclassifiable,
       CASE WHEN covered = 1 THEN history_not_read ELSE NULLIF(history_not_read, 0) END AS history_not_read
FROM counted
ORDER BY sprint_id, position;
