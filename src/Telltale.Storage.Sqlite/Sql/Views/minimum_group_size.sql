-- The minimum group size (GLOSSARY, ADR-0003 as amended by ADR-0008), stated once: every metric view reads
-- its two numbers from this one row and none writes them out. The numbers are the ones the rebuild recorded
-- (run.minimum_team, run.minimum_assignees_behind_a_figure): the configuration's minimumGroupSize, five and
-- three when it has none. team is the smallest team whose results are shown at all: the distinct assignees
-- of a sprint's issues (sprint_team) or, for the footer's counts, of the whole store.
-- assignees_behind_a_figure is the smallest number of distinct assignees among the issues one figure is
-- computed from. A figure is shown only when both hold. A number of zero turns its check off: every count
-- of assignees is at least zero (owner decision D65).
CREATE VIEW minimum_group_size AS
SELECT minimum_team                      AS team,
       minimum_assignees_behind_a_figure AS assignees_behind_a_figure
FROM run;
