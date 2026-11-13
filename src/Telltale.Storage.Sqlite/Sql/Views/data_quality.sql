-- One row for the report's method note and footer: the as-of time of the rebuild and the time zone its
-- business days were counted in, how many issues have at least one changelog anomaly, and how many were
-- delivered outside every sprint window. Both counts are over the whole store, since an issue delivered
-- between sprints belongs to no sprint.
-- Minimum group size (GLOSSARY, ADR-0003; the numbers are minimum_group_size's): the two counts are results
-- about the whole store, grouped by an attribute of issues, so the team behind them is every assignee in
-- the store, and each count also needs the distinct assignees of the issues it counts. suppressed is the
-- team check: 1 when the store has fewer assignees than a team, and then both counts are null. With a team,
-- a count is null when fewer than the assignees a figure needs are among its issues; a count of nothing has
-- nobody behind it and is null too. An unassigned issue counts toward nobody. (sprint_team is the team
-- check per sprint.)
CREATE VIEW data_quality AS
WITH store_team AS (
    SELECT CASE WHEN COUNT(DISTINCT assignee_hash) >= (SELECT team FROM minimum_group_size) THEN 0 ELSE 1 END AS suppressed
    FROM issue
),
anomalous AS (
    SELECT COUNT(DISTINCT a.issue_id) AS issues, COUNT(DISTINCT i.assignee_hash) AS assignees
    FROM anomaly a
    LEFT JOIN issue i ON i.issue_id = a.issue_id
),
between_sprints AS (
    SELECT COUNT(*) AS issues, COUNT(DISTINCT i.assignee_hash) AS assignees
    FROM issue_cycle c
    LEFT JOIN issue i ON i.issue_id = c.issue_id
    WHERE c.delivered = 1 AND c.delivery_sprint_id IS NULL
)
SELECT (SELECT as_of FROM run) AS as_of,
       (SELECT time_zone FROM run) AS time_zone,
       CASE WHEN t.suppressed = 0 AND a.assignees >= g.assignees_behind_a_figure THEN a.issues END AS anomaly_issue_count,
       CASE WHEN t.suppressed = 0 AND b.assignees >= g.assignees_behind_a_figure THEN b.issues END AS delivered_between_sprints_count,
       t.suppressed
FROM store_team t, anomalous a, between_sprints b, minimum_group_size g;
