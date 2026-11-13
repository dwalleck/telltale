-- One row per sprint that counts (see sprint_window): what the report's title needs, and how many
-- issues were delivered in the sprint's window. started and ended are the window's: ended is null
-- while the sprint is not completed. When a sprint ends is decided by derive alone (SprintWindows.cs).
-- planned_end is Jira's planned end, for information only: no figure is counted to it.
-- Minimum group size (GLOSSARY, ADR-0003; owner decision D49): delivered_count is null and suppressed is 1
-- when sprint_team says so (delivered_suppressed): the sprint has fewer than five distinct assignees, or
-- fewer than three are among the delivered issues. team_suppressed is the team check alone
-- (sprint_team.suppressed): 1 for a sprint whose tables both show no rows.
CREATE VIEW delivered_per_sprint AS
SELECT w.sprint_id,
       s.name        AS sprint_name,
       s.state,
       w.started,
       w.ended,
       s.planned_end,
       CASE WHEN t.delivered_suppressed = 0 THEN COUNT(c.issue_id) END AS delivered_count,
       t.delivered_suppressed AS suppressed,
       t.suppressed           AS team_suppressed
FROM sprint_window w
JOIN sprint s ON s.sprint_id = w.sprint_id
JOIN sprint_team t ON t.sprint_id = w.sprint_id
LEFT JOIN issue_cycle c ON c.delivery_sprint_id = w.sprint_id AND c.delivered = 1
GROUP BY w.sprint_id;
