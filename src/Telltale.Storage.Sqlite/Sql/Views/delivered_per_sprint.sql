-- One row per sprint that has started (by its state: a future sprint may carry planned dates): what the report's title needs, and how many issues were
-- delivered in the sprint's window.
CREATE VIEW delivered_per_sprint AS
SELECT s.sprint_id,
       s.name        AS sprint_name,
       s.state,
       s.started,
       s.planned_end,
       s.completed,
       COUNT(c.issue_id) AS delivered_count
FROM sprint s
LEFT JOIN issue_cycle c ON c.delivery_sprint_id = s.sprint_id AND c.delivered = 1
WHERE s.state <> 'future' AND s.started IS NOT NULL
GROUP BY s.sprint_id;
