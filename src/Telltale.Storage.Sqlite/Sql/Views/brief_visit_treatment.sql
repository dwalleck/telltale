-- The treatment of brief visits the derived data was built under (GLOSSARY: Treatment; owner decisions D83 and
-- D103), stated once for every reader of the store: one row, with one value for time in status and one for
-- cycle time, each 'count' or 'exclude'. A figure of time_in_status_per_sprint or
-- cycle_time_per_sprint_and_bucket read through query looks the same under either treatment, and this view
-- says which it was measured under. The values are the ones the rebuild recorded (run.brief_visit_treatment),
-- never the configuration's: both are 'count' when the setting was left out, and when no status is listed.
-- A metric view that needs the treatment reads it from here (brief_visits_per_sprint does).
-- The view holds nothing about a person or an issue, so it has no suppressed flag, and the minimum group size
-- does not apply to it.
CREATE VIEW brief_visit_treatment AS
SELECT json_extract(brief_visit_treatment, '$.timeInStatus') AS time_in_status,
       json_extract(brief_visit_treatment, '$.cycleTime')    AS cycle_time
FROM run;
