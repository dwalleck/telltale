-- The baseline sprints (GLOSSARY: Baseline): one row for each sprint of the list the rebuild recorded
-- (run.baseline_sprint_ids, a JSON array); no row when no baseline is configured. The baseline views pool
-- over these rows and never read the list themselves.
-- suppressed is the one condition under which the whole baseline is withheld (GLOSSARY: Minimum group size;
-- owner decisions D50, D56): 1 when the sprint's delivered count is withheld (sprint_team.delivered_suppressed:
-- fewer than five distinct assignees, or fewer than three among its delivered issues), or it is not a sprint
-- that counts. While any row here has suppressed = 1, every baseline figure is withheld, and the baseline
-- views have no rows at all: rows for some buckets or statuses would say what such a sprint has, which its
-- own report withholds. The console and the report name the sprint.
CREATE VIEW baseline_sprint AS
SELECT b.value                             AS sprint_id,
       COALESCE(t.delivered_suppressed, 1) AS suppressed
FROM json_each((SELECT baseline_sprint_ids FROM run)) b
LEFT JOIN sprint_team t ON t.sprint_id = b.value;
