-- The minimum group size (GLOSSARY, ADR-0003), stated once: every metric view reads its two numbers from
-- this one row and none writes them out. team is the smallest team whose results are shown at all: the
-- distinct assignees of a sprint's issues (sprint_team) or, for the footer's counts, of the whole store.
-- assignees_behind_a_figure is the smallest number of distinct assignees among the issues one figure is
-- computed from. A figure is shown only when both hold.
CREATE VIEW minimum_group_size AS
SELECT 5 AS team,
       3 AS assignees_behind_a_figure;
