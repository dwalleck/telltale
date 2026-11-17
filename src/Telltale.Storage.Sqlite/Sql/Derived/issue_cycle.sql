-- cycle_end is the issue's last move into a done status. It is null when the issue is not done now, and
-- also when it is in a done status but its changelog ends in a status that is not done (owner decision D8)
-- or holds no move into done at all (D9): such an issue is not delivered. delivered says
-- whether that was a delivery: an abandoned issue has a cycle_end and delivered = 0. Filter on delivered,
-- never on cycle_end. delivery_sprint_id is the sprint whose window contains a delivery: null when it
-- fell between sprints, and for every issue that is not delivered.
-- cycle_start, calendar_days and business_days are null together: the issue has no cycle time (it is not
-- delivered, or it never entered a work status, or its cycle starts with a brief visit and the treatment for
-- cycle time leaves such cycles out: GLOSSARY: Treatment, owner decision D85). An issue of the last kind is
-- delivered all the same, and its point_bucket is what it is when the cycle is counted (owner decision D102).
-- point_bucket is set for delivered issues only: null for
-- one that is unestimated, and for every issue that is not delivered, whatever its estimate. It is the
-- estimate at the start of the issue's cycle, also when that cycle was left out, and the estimate at the
-- delivery moment for an issue that never entered a work status (owner decision D35).
-- reopen_count is the number of transitions out of a done status, for every issue, delivered or not.
-- starts_with_brief_visit is 1 when the issue is delivered and its cycle starts with a brief visit (GLOSSARY:
-- Brief visit; owner decision D90): the stay that begins at the cycle's start is a brief visit under its
-- status's threshold. Derive decides (Cycles.cs); cycle_starts_with_brief_visit_per_sprint counts the issues
-- and applies the minimum group size. It is 0 for every issue when no status is listed. It is set whatever
-- the treatment for cycle time, also for an issue whose cycle time was left out for it, so that the number of
-- such issues is the same under either treatment (owner decision D87). It is a flag and nothing else: no
-- duration, no time.
CREATE TABLE issue_cycle (
    issue_id           TEXT PRIMARY KEY,
    cycle_start        TEXT NULL,
    cycle_end          TEXT NULL,
    calendar_days      REAL NULL,
    business_days      REAL NULL,
    point_bucket       REAL NULL,
    delivered          INTEGER NOT NULL,
    delivery_sprint_id TEXT NULL,
    reopen_count       INTEGER NOT NULL,
    starts_with_brief_visit INTEGER NOT NULL
) STRICT;
