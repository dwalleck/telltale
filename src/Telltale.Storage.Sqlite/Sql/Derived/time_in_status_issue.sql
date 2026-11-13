-- Not a metric view: one row per issue, which only the views that apply the minimum group size may read.
-- It says, once, which issues are in a status's time in status row for a sprint (GLOSSARY: Time in status):
-- an issue counts toward a status only if it spent business time there (owner decision D21). One that was in
-- the status only over a weekend or a holiday has no row here, so it is in neither the row's count nor its
-- median, adds no total, and is not counted among the row's issues with a brief visit. Every view that is
-- about the issues of a row reads this one: duration_sample, time_in_status_per_sprint and
-- brief_visits_per_sprint. Done statuses are here too; the views leave them out.
CREATE VIEW time_in_status_issue AS
SELECT t.sprint_id,
       t.status_id,
       t.issue_id,
       t.business_days
FROM sprint_status_time t
WHERE t.business_days > 0;
