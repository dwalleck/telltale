-- Brief visits (GLOSSARY: Brief visit; owner decisions D58 to D62). Per sprint and status listed in
-- briefVisitThresholds that had a time in status row before any brief visit was left out: the status's
-- threshold in minutes, and how many of the issues in that row have at least one brief visit to the status
-- that counts toward the sprint (brief_visit). Which issues were in that row is time_in_status_issue_recorded's
-- to say (owner decisions D21, D93), so the count is never larger than the row's number of issues. While brief
-- visits are counted and nothing is left out (GLOSSARY: Treatment), that row is the status's row in
-- time_in_status_per_sprint. When they are left out of time in status, the row there is of what remains, and
-- this view still has a row for a status whose every issue was left out, so that the count explains it.
-- Order by board_position for board order. No figure of any other view is changed by this one.
-- The listed statuses and their thresholds are the ones the rebuild recorded (run.brief_visit_thresholds):
-- this view never reads the configuration. With no status listed it has no rows.
-- Minimum group size (GLOSSARY, ADR-0003; the numbers are minimum_group_size's, the configured ones, five and
-- three by default): a sprint short of a team (sprint_team.team_suppressed) has no row here, as it has no
-- status rows in time_in_status_per_sprint, and a done status has none. brief_visit_issue_count is null and
-- suppressed is 1 when fewer distinct assignees than a figure needs are among the issues counted. That one
-- check is also the owner's other rule (D62, D99: withheld when the median of the row the count is measured
-- against would be withheld): the issues counted are among that row's issues, so a row with too few assignees
-- behind it has no more behind the issues counted, and the rule on a row is stated in
-- time_in_status_per_sprint and not a second time here. The row the count is measured against is the one
-- before anything was left out, so a count can be shown beside a row of time_in_status_per_sprint that is
-- suppressed for what remains (D99). While that number is one or more, a
-- count of nothing has nobody behind it and is null too, so a zero reads as a withheld count does; a number
-- of zero turns the check off (owner decision D65), and a count of nothing is then shown as 0, as
-- data_quality's counts are. An unassigned issue counts toward nobody. The check of the delivered count
-- (sprint_team.delivered_suppressed, D49) is not read, as time_in_status_per_sprint does not read it (D55).
CREATE VIEW brief_visits_per_sprint AS
WITH threshold AS (
    SELECT CAST(j.key AS TEXT) AS status_id, CAST(j.value AS REAL) AS threshold_minutes
    FROM json_each((SELECT brief_visit_thresholds FROM run)) j
),
-- The rows the counts are measured against: one per sprint and status some issue spent business time in, as
-- recorded. It says which sprints and statuses have a row here, and nothing about a row's figures.
recorded_row AS (
    SELECT DISTINCT t.sprint_id, t.status_id
    FROM time_in_status_issue_recorded t
),
counted AS (
    SELECT v.sprint_id,
           v.status_id,
           COUNT(*) AS issues,
           COUNT(DISTINCT i.assignee_hash) AS assignees
    FROM brief_visit v
    JOIN time_in_status_issue_recorded t ON t.issue_id = v.issue_id AND t.sprint_id = v.sprint_id AND t.status_id = v.status_id
    LEFT JOIN issue i ON i.issue_id = v.issue_id
    GROUP BY v.sprint_id, v.status_id
),
-- Whether a row's count is withheld is decided once, here, and the count and the flag both follow from it.
-- The counted issues and the rows are read from the one view (time_in_status_issue_recorded), so the counted
-- issues are always among the row's issues. Before a treatment could leave issues out, this view read the
-- row's own flag from time_in_status_per_sprint as a guard; that flag is now of what remains, and a copy of
-- the row's rule here would be a second statement of it that could never decide a row.
-- The team check and the done statuses left out are read from where they are stated (sprint_team,
-- board_status), and the status's name and position are joined as time_in_status_per_sprint joins them: this
-- view cannot take them from that view, whose rows are of what remains.
decided AS (
    SELECT r.sprint_id,
           r.status_id,
           COALESCE(s.name, r.status_id) AS status_name,
           b.position AS board_position,
           h.threshold_minutes,
           COALESCE(c.issues, 0) AS issues,
           CASE WHEN COALESCE(c.assignees, 0) >= g.assignees_behind_a_figure THEN 0 ELSE 1 END AS suppressed
    FROM recorded_row r
    JOIN threshold h ON h.status_id = r.status_id
    JOIN sprint_team t ON t.sprint_id = r.sprint_id
    JOIN board_status b ON b.status_id = r.status_id
    LEFT JOIN status s ON s.status_id = r.status_id
    LEFT JOIN counted c ON c.sprint_id = r.sprint_id AND c.status_id = r.status_id
    CROSS JOIN minimum_group_size g
    WHERE b.is_done = 0
      AND t.team_suppressed = 0
)
SELECT d.sprint_id,
       d.status_id,
       d.status_name,
       d.board_position,
       d.threshold_minutes,
       CASE WHEN d.suppressed = 0 THEN d.issues END AS brief_visit_issue_count,
       d.suppressed AS suppressed
FROM decided d;
