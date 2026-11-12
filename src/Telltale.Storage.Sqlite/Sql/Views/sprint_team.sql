-- The team check of the minimum group size (GLOSSARY, ADR-0003): one row per sprint that counts, with
-- suppressed = 1 when the sprint has fewer distinct assignees than a team (minimum_group_size.team, five).
-- Every metric view joins this
-- view and withholds all its figures for such a sprint, so the rule is written once.
-- An issue counts toward its assignee at the time of the sync; an unassigned issue counts toward nobody.
CREATE VIEW sprint_team AS
WITH sprint_issue AS (
    -- Every issue of the sprint, not only the delivered ones (owner decision D6): an issue that spent time
    -- in the sprint as a member, whatever became of it, and an issue delivered in the sprint's window,
    -- member or not, since its figures are reported under this sprint.
    SELECT t.sprint_id, t.issue_id
    FROM sprint_status_time t
    UNION
    SELECT c.delivery_sprint_id, c.issue_id
    FROM issue_cycle c
    WHERE c.delivered = 1
)
SELECT w.sprint_id,
       CASE WHEN COUNT(DISTINCT i.assignee_hash) >= (SELECT team FROM minimum_group_size) THEN 0 ELSE 1 END AS suppressed
FROM sprint_window w
LEFT JOIN sprint_issue si ON si.sprint_id = w.sprint_id
LEFT JOIN issue i ON i.issue_id = si.issue_id
GROUP BY w.sprint_id;
