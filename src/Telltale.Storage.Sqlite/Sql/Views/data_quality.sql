-- One row for the report's method note and footer: the as-of time of the rebuild and the time zone its
-- business days were counted in, how many issues have at least one changelog anomaly, and how many were
-- delivered outside every sprint window. Both counts are over the whole store, since an issue delivered
-- between sprints belongs to no sprint.
-- Minimum group size (GLOSSARY, ADR-0003): the two counts are results about the whole store, so the team
-- behind them is every assignee in it. With fewer than five distinct assignees both are null and
-- suppressed is 1. An unassigned issue counts toward nobody. (sprint_team is the same check per sprint.)
CREATE VIEW data_quality AS
WITH store_team AS (
    SELECT CASE WHEN COUNT(DISTINCT assignee_hash) >= 5 THEN 0 ELSE 1 END AS suppressed
    FROM issue
)
SELECT (SELECT as_of FROM run) AS as_of,
       (SELECT time_zone FROM run) AS time_zone,
       CASE WHEN t.suppressed = 0 THEN (SELECT COUNT(DISTINCT issue_id) FROM anomaly) END AS anomaly_issue_count,
       CASE WHEN t.suppressed = 0 THEN (SELECT COUNT(*) FROM issue_cycle WHERE delivered = 1 AND delivery_sprint_id IS NULL) END AS delivered_between_sprints_count,
       t.suppressed
FROM store_team t;
