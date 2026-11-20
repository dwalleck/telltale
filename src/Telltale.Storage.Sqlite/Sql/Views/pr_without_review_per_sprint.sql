-- Pull requests without a first code review, per sprint: the numbers that explain how many pull requests are
-- behind the first code review figure of pr_timing_per_sprint. One row per sprint that counts and host scope,
-- when a pull request host is configured; no rows otherwise. host_scope is 'github' or 'ado' for one
-- configured host's pull requests, and 'combined' for those of every configured host together; there are
-- 'combined' rows only when more than one host is configured, as in pr_timing_per_sprint.
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
--                          Ops group's members could not be read), so whether it was reviewed is not known.
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
-- Like the pull request figures, these counts are not subject to the minimum group size (owner decision
-- D114): the view applies neither check and has no suppressed flag. It reads no person.
CREATE VIEW pr_without_review_per_sprint AS
WITH scope AS (
    SELECT j.key AS host_scope, j.key AS host FROM run r, json_each(r.pull_request_repositories) j
    UNION ALL
    SELECT 'combined', j.key FROM run r, json_each(r.pull_request_repositories) j
    WHERE (SELECT COUNT(*) FROM run r2, json_each(r2.pull_request_repositories)) > 1
)
SELECT s.sprint_id,
       c.host_scope,
       COUNT(CASE WHEN p.state = 'waiting' THEN 1 END) AS waiting,
       COUNT(CASE WHEN p.state = 'closed without review' THEN 1 END) AS closed_without_review,
       COUNT(CASE WHEN p.state IN ('waiting', 'closed without review') AND p.activity = 'none' THEN 1 END) AS no_activity,
       COUNT(CASE WHEN p.state IN ('waiting', 'closed without review') AND p.activity = 'automation only' THEN 1 END) AS automation_only,
       CASE WHEN c.host_scope = 'ado' THEN NULL
            ELSE COUNT(CASE WHEN p.state IN ('waiting', 'closed without review') AND p.activity = 'ops only' THEN 1 END)
       END AS ops_only,
       COUNT(CASE WHEN p.state = 'unclassifiable' THEN 1 END) AS unclassifiable,
       COUNT(CASE WHEN p.state = 'history not read' THEN 1 END) AS history_not_read
FROM sprint_window s
CROSS JOIN scope c
LEFT JOIN pr_without_review p ON p.sprint_id = s.sprint_id AND p.host = c.host
GROUP BY s.sprint_id, c.host_scope
ORDER BY s.sprint_id, CASE c.host_scope WHEN 'github' THEN 1 WHEN 'ado' THEN 2 ELSE 3 END;
