-- One row per sprint that counts (see sprint_window): what the report's title needs, and how many
-- issues were delivered in the sprint's window. started and ended are the window's: ended is null
-- while the sprint is not completed. When a sprint ends is decided by derive alone (Derivation.cs).
-- planned_end is Jira's planned end, for information only: no figure is counted to it.
-- The count is a figure about the whole sprint, so only the team check applies to it (sprint_team): for a
-- sprint with fewer than five distinct assignees delivered_count is null and suppressed is 1 (ADR-0003).
CREATE VIEW delivered_per_sprint AS
SELECT w.sprint_id,
       s.name        AS sprint_name,
       s.state,
       w.started,
       w.ended,
       s.planned_end,
       CASE WHEN t.suppressed = 0 THEN COUNT(c.issue_id) END AS delivered_count,
       t.suppressed
FROM sprint_window w
JOIN sprint s ON s.sprint_id = w.sprint_id
JOIN sprint_team t ON t.sprint_id = w.sprint_id
LEFT JOIN issue_cycle c ON c.delivery_sprint_id = w.sprint_id AND c.delivered = 1
GROUP BY w.sprint_id;
