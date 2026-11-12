-- The sprints the baseline is drawn from (GLOSSARY: Baseline), one row each; no row when no baseline is
-- configured. It says which list the baseline views were built from, so a report can tell when the
-- configuration has changed since.
CREATE VIEW baseline_sprints AS
SELECT sprint_id
FROM baseline_sprint;
