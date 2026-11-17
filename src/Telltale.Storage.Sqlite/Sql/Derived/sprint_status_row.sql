-- Not a metric view: it holds no figure, and only the metric views read it.
-- It says, once, which sprint and status may have a row in a sprint's time in status table, and how the row
-- is named and ordered: a sprint that is not short of a team (sprint_team.team_suppressed; a sprint short of
-- one has no status rows at all, owner decisions D41 and D45), and a status the configuration gives a role
-- other than done (board_status; an issue in a done status is not waiting on the team). status_name is
-- Jira's name for the status, or its ID when Jira lists none, and board_position is board order.
-- Whether an issue spent time in the status is not asked here: a view joins the rows it has to this one.
-- Two metric views read it, so that they cannot disagree about which statuses a sprint's table may hold:
-- time_in_status_per_sprint, for the rows of what remains once any brief visit has been left out, and
-- brief_visits_per_sprint, for the rows before anything was left out. The report's sentence that a listed
-- status whose every issue was left out has a footer line and no row rests on the two agreeing (owner
-- decision D98).
-- Only the team check is read, not the check of the delivered count (sprint_team.delivered_suppressed):
-- the counts of both views are of every issue that spent time in a status, delivered or not (owner
-- decision D55).
CREATE VIEW sprint_status_row AS
SELECT t.sprint_id,
       b.status_id,
       COALESCE(s.name, b.status_id) AS status_name,
       b.position                    AS board_position
FROM sprint_team t
CROSS JOIN board_status b
LEFT JOIN status s ON s.status_id = b.status_id
WHERE b.is_done = 0
  AND t.team_suppressed = 0;
