-- Not a metric view: one row per issue, which only the views that apply the minimum group size may read.
-- It says, once, which issues are in a status's time in status row for a sprint (GLOSSARY: Time in status):
-- an issue counts toward a status only if it spent business time there (owner decision D21). One that was in
-- the status only over a weekend or a holiday has no row here, so it is in neither the row's count nor its
-- median, and adds no total. The business time is the measured one (sprint_status_time.measured_business_days):
-- when the treatment for time in status leaves brief visits out (GLOSSARY: Treatment; owner decision D84), an
-- issue whose only business time in a listed status was brief visits has no row here either, and one with a
-- longer stay there as well is here with what remains. Every view that gives a time in status figure reads
-- this one: duration_sample and time_in_status_per_sprint. The brief visit count is measured against the
-- rows before anything was left out, and reads time_in_status_issue_recorded. Done statuses are here too;
-- the views leave them out.
CREATE VIEW time_in_status_issue AS
SELECT t.sprint_id,
       t.status_id,
       t.issue_id,
       t.measured_business_days AS business_days
FROM sprint_status_time t
WHERE t.measured_business_days > 0;
