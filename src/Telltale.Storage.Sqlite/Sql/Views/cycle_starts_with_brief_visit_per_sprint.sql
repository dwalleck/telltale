-- Cycles that start with a brief visit (GLOSSARY: Brief visit, Cycle time; owner decisions D87 and D90). Per
-- sprint that counts: how many of the issues delivered in the sprint have a cycle that starts with a brief
-- visit (issue_cycle.starts_with_brief_visit, which derive decides: the stay that begins at the cycle's start
-- is a brief visit under its status's threshold). The number says how many cycles start when the board was
-- updated and not when the work started (owner decision D107): that usually makes a cycle too short, and
-- where an issue was moved into a work status and straight back out before the work began (D91) it makes
-- it too long. It is the same whatever the treatment for cycle time (GLOSSARY: Treatment): under count the issues
-- it counts are in the cycle time figures, and under exclude they have no cycle time (owner decision D85).
-- No figure of any other view is changed by this one.
-- The view has rows only when a status listed in briefVisitThresholds has the work role: a cycle starts in a
-- work status, so with none listed no cycle can start with a brief visit, and a row would say nothing. The
-- listed statuses are the ones the rebuild recorded (run.brief_visit_thresholds) and their roles are
-- board_status's: this view never reads the configuration.
-- Minimum group size (GLOSSARY, ADR-0003; the numbers are minimum_group_size's, the configured ones, five and
-- three by default): issue_count is null and suppressed is 1 when the sprint's delivered count is withheld
-- (sprint_team.delivered_suppressed, which holds the team check; owner decision D49), or fewer distinct
-- assignees than a figure needs are among the issues counted. While that number is one or more, a count of
-- nothing has nobody behind it and is null too, so a zero reads as a withheld count does; a number of zero
-- turns the check off (owner decision D65), and a count of nothing is then shown as 0. An unassigned issue
-- counts toward nobody. This number can be combined with shown counts of brief_visits_per_sprint to say
-- something about a withheld one: the issues it counts are often among those the per-status counts count, and
-- the subtraction then gives the withheld count or a bound on it. They are not always among them: a per-status
-- count needs the issue in the status's time in status row and the visit inside the time the sprint counts,
-- and this number needs only a delivery in the sprint. The owner accepted the combination, and the number is
-- not withheld for it (owner decision D97).
CREATE VIEW cycle_starts_with_brief_visit_per_sprint AS
WITH counted AS (
    SELECT c.delivery_sprint_id AS sprint_id,
           COUNT(*) AS issues,
           COUNT(DISTINCT i.assignee_hash) AS assignees
    FROM issue_cycle c
    LEFT JOIN issue i ON i.issue_id = c.issue_id
    WHERE c.delivered = 1 AND c.starts_with_brief_visit = 1
    GROUP BY c.delivery_sprint_id
),
-- Whether a sprint's count is withheld is decided once, here, and the count and the flag both follow from it.
decided AS (
    SELECT w.sprint_id,
           COALESCE(c.issues, 0) AS issues,
           CASE WHEN t.delivered_suppressed = 0 AND COALESCE(c.assignees, 0) >= g.assignees_behind_a_figure THEN 0 ELSE 1 END AS suppressed
    FROM sprint_window w
    JOIN sprint_team t ON t.sprint_id = w.sprint_id
    LEFT JOIN counted c ON c.sprint_id = w.sprint_id
    CROSS JOIN minimum_group_size g
    WHERE EXISTS (
        SELECT 1
        FROM json_each((SELECT brief_visit_thresholds FROM run)) j
        JOIN board_status b ON b.status_id = CAST(j.key AS TEXT)
        WHERE b.is_work = 1
    )
)
SELECT d.sprint_id,
       CASE WHEN d.suppressed = 0 THEN d.issues END AS issue_count,
       d.suppressed AS suppressed
FROM decided d;
