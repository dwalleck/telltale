-- One row for the report's footer: the as-of time of the rebuild, how many issues have at least one
-- changelog anomaly, and how many were delivered outside every sprint window. Both counts are over
-- the whole store, since an issue delivered between sprints belongs to no sprint.
CREATE VIEW data_quality AS
SELECT (SELECT as_of FROM run) AS as_of,
       (SELECT COUNT(DISTINCT issue_id) FROM anomaly) AS anomaly_issue_count,
       (SELECT COUNT(*) FROM issue_cycle WHERE delivered = 1 AND delivery_sprint_id IS NULL) AS delivered_between_sprints_count;
