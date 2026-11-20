# 06: Report sprint timing distributions

**What to build:** For every available PR timing measure show the sample count, median and nearest-rank P85, independently in elapsed and business days. This slice can use the GitHub review and merge measures before approval and ADO ship.

**Blocked by:** 03 — Report an unlinked GitHub PR across sprints.

**Status:** resolved

- [x] Through the CLI report and protected metric views, verify hand-calculated synthetic waits, including an even-sized median and a weekend or company holiday in the configured team time zone. Rank unrounded individual waits separately for each clock.
- [x] Count the full wait once in the window containing its end, inclusive start and exclusive end. Keep events in a gap or overlapping windows unassigned; never clip the start to the sprint.
- [x] Respect the run’s as-of instant. No qualifying observations means no duration figure, not a duration of zero. Label units and contributing PR counts. PR figures are not subject to the minimum group size (owner decision D114).
