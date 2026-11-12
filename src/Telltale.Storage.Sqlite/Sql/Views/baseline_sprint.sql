-- The baseline sprints (GLOSSARY: Baseline): one row for each sprint of the list the rebuild recorded
-- (run.baseline_sprint_ids, a JSON array); no row when no baseline is configured. The baseline views pool
-- over these rows and never read the list themselves.
-- suppressed is the sprint's team check (sprint_team): 1 when the sprint has fewer than five distinct
-- assignees, or is not a sprint that counts. A baseline sprint must pass the team check (GLOSSARY: Minimum
-- group size; owner decision D50): while any row here has suppressed = 1, every baseline figure is withheld,
-- and the baseline views have no rows at all. The report names such a sprint.
CREATE VIEW baseline_sprint AS
SELECT b.value                   AS sprint_id,
       COALESCE(t.suppressed, 1) AS suppressed
FROM json_each((SELECT baseline_sprint_ids FROM run)) b
LEFT JOIN sprint_team t ON t.sprint_id = b.value;
