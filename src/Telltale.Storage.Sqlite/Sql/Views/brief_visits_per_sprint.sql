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
-- The number of issues left out of the row (GLOSSARY: Treatment; owner decisions D84, D95, D98):
-- left_out_issue_count is how many of the issues that were in the row before anything was left out are not
-- in the status's row in time_in_status_per_sprint, because their only business time in the status was brief
-- visits. Each of them is among the issues with a brief visit. It is measured only when the rebuild left brief
-- visits out of time in status (brief_visit_treatment): while they are counted nothing is left out, the
-- number is null, and left_out_suppressed is 0, because nothing is withheld. When they are left out, the
-- number is null and left_out_suppressed is 1 when fewer distinct assignees than a figure needs are among the
-- issues left out, by the same check and the same numbers as the count: so a number of nothing reads as a
-- withheld one does, and with the number of assignees at zero it is shown as 0. A row of this view with no
-- row in time_in_status_per_sprint shows that every issue of the status was left out, so that its number and
-- its count are each at least one also when they are withheld. A shown count larger than the issue count of
-- the status's row there shows that the number is at least their difference, and a count shown beside a row
-- that is suppressed there shows that it is at least one. The owner accepted each, and no count is withheld
-- for it (D98).
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
-- Whether the rebuild left brief visits out of time in status: the treatment it recorded, never the
-- configuration's, read from the one view that gives it.
treatment AS (
    SELECT time_in_status = 'exclude' AS leaves_out
    FROM brief_visit_treatment
),
-- The issues left out of a row: in it before anything was left out, and not in what remains.
left_out AS (
    SELECT r.sprint_id,
           r.status_id,
           COUNT(*) AS issues,
           COUNT(DISTINCT a.assignee_hash) AS assignees
    FROM time_in_status_issue_recorded r
    LEFT JOIN issue a ON a.issue_id = r.issue_id
    WHERE NOT EXISTS (
        SELECT 1
        FROM time_in_status_issue m
        WHERE m.issue_id = r.issue_id AND m.sprint_id = r.sprint_id AND m.status_id = r.status_id
    )
    GROUP BY r.sprint_id, r.status_id
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
           -- The number of issues left out is decided here in the same way, once. While brief visits are
           -- counted there is no number and nothing is withheld.
           CASE WHEN treatment.leaves_out THEN COALESCE(l.issues, 0) END AS left_out,
           CASE WHEN NOT treatment.leaves_out OR COALESCE(l.assignees, 0) >= g.assignees_behind_a_figure THEN 0 ELSE 1 END AS left_out_suppressed,
           COALESCE(c.issues, 0) AS issues,
           CASE WHEN COALESCE(c.assignees, 0) >= g.assignees_behind_a_figure THEN 0 ELSE 1 END AS suppressed
    FROM recorded_row r
    JOIN threshold h ON h.status_id = r.status_id
    JOIN sprint_team t ON t.sprint_id = r.sprint_id
    JOIN board_status b ON b.status_id = r.status_id
    LEFT JOIN status s ON s.status_id = r.status_id
    LEFT JOIN counted c ON c.sprint_id = r.sprint_id AND c.status_id = r.status_id
    LEFT JOIN left_out l ON l.sprint_id = r.sprint_id AND l.status_id = r.status_id
    CROSS JOIN treatment
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
       d.suppressed AS suppressed,
       CASE WHEN d.left_out_suppressed = 0 THEN d.left_out END AS left_out_issue_count,
       d.left_out_suppressed AS left_out_suppressed
FROM decided d;
