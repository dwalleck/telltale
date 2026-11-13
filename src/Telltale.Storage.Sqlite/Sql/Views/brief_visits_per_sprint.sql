-- Brief visits (GLOSSARY: Brief visit; owner decisions D58 to D62). Per sprint and status listed in
-- briefVisitThresholds that has a row in time_in_status_per_sprint: the status's threshold in minutes, and how
-- many of the issues in that row have at least one brief visit to the status that counts toward the sprint
-- (brief_visit). Which issues are in the row is time_in_status_issue's to say, here as in
-- time_in_status_per_sprint (owner decision D21), so the count is never larger than the row's issue_count. Order by board_position for board order. No figure of any other view is changed by this one.
-- The listed statuses and their thresholds are the ones the rebuild recorded (run.brief_visit_thresholds):
-- this view never reads the configuration. With no status listed it has no rows.
-- Minimum group size (GLOSSARY, ADR-0003; the numbers are minimum_group_size's, the configured ones, five and
-- three by default): the rows come from time_in_status_per_sprint, so a sprint short of a team has no row
-- here, and a done status has none. brief_visit_issue_count is null and suppressed is 1 when the status's
-- row in time_in_status_per_sprint is suppressed, or fewer distinct assignees than a figure needs are among
-- the issues counted. While that number is one or more, a count of nothing has nobody behind it and is null
-- too, so a zero reads as a withheld count does; a number of zero turns the check off (owner decision D65),
-- and a count of nothing is then shown as 0, as data_quality's counts are. An unassigned issue counts
-- toward nobody. The check of the delivered count (sprint_team.delivered_suppressed, D49) is not read, as
-- time_in_status_per_sprint does not read it (D55).
CREATE VIEW brief_visits_per_sprint AS
WITH threshold AS (
    SELECT CAST(j.key AS TEXT) AS status_id, CAST(j.value AS REAL) AS threshold_minutes
    FROM json_each((SELECT brief_visit_thresholds FROM run)) j
),
counted AS (
    SELECT v.sprint_id,
           v.status_id,
           COUNT(*) AS issues,
           COUNT(DISTINCT i.assignee_hash) AS assignees
    FROM brief_visit v
    JOIN time_in_status_issue t ON t.issue_id = v.issue_id AND t.sprint_id = v.sprint_id AND t.status_id = v.status_id
    LEFT JOIN issue i ON i.issue_id = v.issue_id
    GROUP BY v.sprint_id, v.status_id
),
-- Whether a row's count is withheld is decided once, here, and the count and the flag both follow from it.
-- "r.suppressed = 0" is the rule as the owner stated it (D62: withheld when the row's median is withheld). It
-- is a guard that cannot decide a row on its own while the counted issues are among the row's issues: a row
-- with too few assignees behind it has no more behind the issues counted. It stays so that the rule is
-- written where it is applied, and holds if the two ever stop reading the same issues.
decided AS (
    SELECT r.sprint_id,
           r.status_id,
           r.status_name,
           r.board_position,
           h.threshold_minutes,
           COALESCE(c.issues, 0) AS issues,
           CASE WHEN r.suppressed = 0 AND COALESCE(c.assignees, 0) >= g.assignees_behind_a_figure THEN 0 ELSE 1 END AS suppressed
    FROM time_in_status_per_sprint r
    JOIN threshold h ON h.status_id = r.status_id
    LEFT JOIN counted c ON c.sprint_id = r.sprint_id AND c.status_id = r.status_id
    CROSS JOIN minimum_group_size g
)
SELECT d.sprint_id,
       d.status_id,
       d.status_name,
       d.board_position,
       d.threshold_minutes,
       CASE WHEN d.suppressed = 0 THEN d.issues END AS brief_visit_issue_count,
       d.suppressed AS suppressed
FROM decided d;
