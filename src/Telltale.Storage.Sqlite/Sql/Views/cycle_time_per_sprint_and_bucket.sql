-- Cycle time of the issues delivered in each sprint, by point bucket. One row per sprint and bucket that has
-- at least one cycle time; point_bucket is null for unestimated issues. issue_count is the number of cycle
-- times behind the figures: a delivered issue that never entered a work status has none and is not in it.
--
-- Median: the middle value, or the mean of the two middle values. P85: nearest rank, the smallest value with
-- at least 85% of the values at or below it. Both are picked by integer rank: (n + 1) / 2 and (n + 2) / 2 are
-- the same row for an odd n and the two middle rows for an even n; (85 * n + 99) / 100 is ceil(0.85 * n)
-- without floating point. Business and calendar days are ranked separately, since they can order issues differently.
--
-- Minimum group size (GLOSSARY, ADR-0003): the four figures are null and suppressed is 1 when the sprint has
-- fewer than five distinct assignees (sprint_team) or the bucket has fewer than three behind it. issue_count
-- stays (owner decision D5). An issue counts toward its assignee at the time of the sync, and an unassigned
-- issue toward nobody.
CREATE VIEW cycle_time_per_sprint_and_bucket AS
WITH ranked AS (
    SELECT c.delivery_sprint_id AS sprint_id,
           c.point_bucket,
           c.business_days,
           c.calendar_days,
           i.assignee_hash,
           ROW_NUMBER() OVER (PARTITION BY c.delivery_sprint_id, c.point_bucket ORDER BY c.business_days) AS business_rank,
           ROW_NUMBER() OVER (PARTITION BY c.delivery_sprint_id, c.point_bucket ORDER BY c.calendar_days) AS calendar_rank,
           COUNT(*)     OVER (PARTITION BY c.delivery_sprint_id, c.point_bucket)                          AS n
    FROM issue_cycle c
    LEFT JOIN issue i ON i.issue_id = c.issue_id
    WHERE c.delivered = 1 AND c.business_days IS NOT NULL
),
bucket AS (
    SELECT sprint_id,
           point_bucket,
           COUNT(*) AS issue_count,
           COUNT(DISTINCT assignee_hash) AS assignees,
           AVG(CASE WHEN business_rank IN ((n + 1) / 2, (n + 2) / 2) THEN business_days END) AS median_business_days,
           MAX(CASE WHEN business_rank = (85 * n + 99) / 100 THEN business_days END)         AS p85_business_days,
           AVG(CASE WHEN calendar_rank IN ((n + 1) / 2, (n + 2) / 2) THEN calendar_days END) AS median_calendar_days,
           MAX(CASE WHEN calendar_rank = (85 * n + 99) / 100 THEN calendar_days END)         AS p85_calendar_days
    FROM ranked
    GROUP BY sprint_id, point_bucket
)
SELECT b.sprint_id,
       b.point_bucket,
       b.issue_count,
       CASE WHEN t.suppressed = 0 AND b.assignees >= 3 THEN b.median_business_days END AS median_business_days,
       CASE WHEN t.suppressed = 0 AND b.assignees >= 3 THEN b.p85_business_days END    AS p85_business_days,
       CASE WHEN t.suppressed = 0 AND b.assignees >= 3 THEN b.median_calendar_days END AS median_calendar_days,
       CASE WHEN t.suppressed = 0 AND b.assignees >= 3 THEN b.p85_calendar_days END    AS p85_calendar_days,
       CASE WHEN t.suppressed = 0 AND b.assignees >= 3 THEN 0 ELSE 1 END               AS suppressed
FROM bucket b
-- An inner join: an issue delivered between sprints has no sprint, so it is in no row.
JOIN sprint_team t ON t.sprint_id = b.sprint_id;
