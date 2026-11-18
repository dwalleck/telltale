-- Per sprint and status: how many issues spent time in the status during the sprint, their median
-- business days (duration_figures' median), and their total business days. A status no issue spent business time in has no row. Order by board_position for board order.
-- Statuses with the done role are left out: an issue in one is not waiting on the team. Their time
-- stays in sprint_status_time and status_interval. Which sprint and status may have a row, and the row's
-- name and position, are sprint_status_row's to say, for this view and for brief_visits_per_sprint.
-- Minimum group size (GLOSSARY, ADR-0003): the median and total are null and suppressed is 1 when the sprint has fewer
-- distinct assignees than a team (sprint_team) or fewer than a figure needs are behind the row's issues
-- (minimum_group_size's two numbers: the configured ones, five and three by default). A row short of the
-- second keeps its issue_count (owner decision D5). A sprint short of a team has no status rows at all: it
-- has exactly one row, with no status, no count (D41) and suppressed = 1, whether or not an issue spent
-- time in it (D45: which statuses have rows would say where its few people's work sat).
-- An unassigned issue counts toward nobody.
-- Only the team check (sprint_team.team_suppressed) is read, here and in sprint_status_row, not the check of the delivered count
-- (delivered_suppressed): these counts are of every issue that spent time in a status, delivered or not, so
-- they stay when the delivered count is withheld, and the report says that a late status's count can be
-- close to it (owner decision D55).
CREATE VIEW time_in_status_per_sprint AS
WITH status_time AS (
    SELECT t.sprint_id,
           t.status_id,
           COUNT(*) AS issue_count,
           SUM(t.business_days) AS total_business_days,
           COUNT(DISTINCT i.assignee_hash) AS assignees
    -- An issue counts toward a status only if it spent business time there (owner decision D21):
    -- time_in_status_issue says which issues those are once any brief visit has been left out, for this view,
    -- for duration_sample and through it the baseline. brief_visits_per_sprint measures its count against the
    -- row before anything was left out (time_in_status_issue_recorded, owner decision D93), and reads this one
    -- only to tell which issues were left out.
    FROM time_in_status_issue t
    LEFT JOIN issue i ON i.issue_id = t.issue_id
    GROUP BY t.sprint_id, t.status_id
)
SELECT st.sprint_id,
       st.status_id,
       r.status_name,
       r.board_position,
       st.issue_count,
       CASE WHEN st.assignees >= (SELECT assignees_behind_a_figure FROM minimum_group_size) THEN f.median_days END AS median_business_days,
       CASE WHEN st.assignees >= (SELECT assignees_behind_a_figure FROM minimum_group_size) THEN st.total_business_days END AS total_business_days,
       CASE WHEN st.assignees >= (SELECT assignees_behind_a_figure FROM minimum_group_size) THEN 0 ELSE 1 END      AS suppressed
FROM status_time st
JOIN sprint_status_row r ON r.sprint_id = st.sprint_id AND r.status_id = st.status_id
JOIN duration_figures f ON f.scope = 'sprint' AND f.measure = 'status' AND f.sprint_id = st.sprint_id AND CAST(f.status_id AS TEXT) = CAST(st.status_id AS TEXT)
UNION ALL
SELECT t.sprint_id, NULL, NULL, NULL, NULL, NULL, NULL, 1
FROM sprint_team t
WHERE t.team_suppressed = 1;
