-- One row per sprint that counts (see sprint_window): what the report's title needs, and how many
-- issues were delivered in the sprint's window. started and ended are the window's: ended is null
-- while the sprint is not completed. When a sprint ends is decided by derive alone (SprintWindows.cs).
-- planned_end is Jira's planned end, for information only: no figure is counted to it.
-- Minimum group size (GLOSSARY, ADR-0003; owner decision D49): delivered_count is null and suppressed is 1
-- when the sprint has fewer than five distinct assignees (sprint_team) or fewer than three distinct assignees
-- are among the delivered issues. A sprint in which nothing was delivered has nobody behind its count, so
-- the count is withheld there too. team_suppressed is the team check alone (sprint_team.suppressed): 1 for
-- a sprint whose tables show no rows at all. An unassigned issue counts toward nobody.
CREATE VIEW delivered_per_sprint AS
SELECT w.sprint_id,
       s.name        AS sprint_name,
       s.state,
       w.started,
       w.ended,
       s.planned_end,
       CASE WHEN t.suppressed = 0 AND COUNT(DISTINCT i.assignee_hash) >= (SELECT assignees_behind_a_figure FROM minimum_group_size) THEN COUNT(c.issue_id) END AS delivered_count,
       CASE WHEN t.suppressed = 0 AND COUNT(DISTINCT i.assignee_hash) >= (SELECT assignees_behind_a_figure FROM minimum_group_size) THEN 0 ELSE 1 END          AS suppressed,
       t.suppressed  AS team_suppressed
FROM sprint_window w
JOIN sprint s ON s.sprint_id = w.sprint_id
JOIN sprint_team t ON t.sprint_id = w.sprint_id
LEFT JOIN issue_cycle c ON c.delivery_sprint_id = w.sprint_id AND c.delivered = 1
LEFT JOIN issue i ON i.issue_id = c.issue_id
GROUP BY w.sprint_id;
