-- One row per sprint that counts (see sprint_window): what the report's title needs, and how many
-- issues were delivered in the sprint's window. started and ended are the window's: ended is null
-- while the sprint is not completed. When a sprint ends is decided by derive alone (Derivation.cs).
CREATE VIEW delivered_per_sprint AS
SELECT w.sprint_id,
       s.name        AS sprint_name,
       s.state,
       w.started,
       w.ended,
       COUNT(c.issue_id) AS delivered_count
FROM sprint_window w
JOIN sprint s ON s.sprint_id = w.sprint_id
LEFT JOIN issue_cycle c ON c.delivery_sprint_id = w.sprint_id AND c.delivered = 1
GROUP BY w.sprint_id;
