-- Not a metric view: one row per issue, which only the views that apply the minimum group size may read.
-- It says, once, which issues were in a status's time in status row for a sprint before any brief visit was
-- left out (GLOSSARY: Treatment): those that spent business time in the status as recorded (owner decision
-- D21). The brief visit count is measured against these rows under either treatment (owner decision D93), so
-- that the issues a treatment leaves out do not vanish from the count that explains them:
-- brief_visits_per_sprint reads this view. While brief visits are counted and nothing is left out, it has
-- the rows of time_in_status_issue. No figure of time is taken from it. Done statuses are here too; the
-- view leaves them out.
CREATE VIEW time_in_status_issue_recorded AS
SELECT t.sprint_id,
       t.status_id,
       t.issue_id
FROM sprint_status_time t
WHERE t.business_days > 0;
