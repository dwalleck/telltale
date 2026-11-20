-- Whether the events a pull request row was written with are in the store (PullRequestHistoryStates): 'read',
-- or 'not read' for a row written by a collection that could not read them, as when the Ops group's members
-- could not be read and no review could be classified. A row that is 'not read' has its events asked for at
-- the next sync whatever its list entry says, and until then no wait that rests on its events is stated.
-- Rows written before this column existed were always written with their events.
ALTER TABLE pull_request ADD COLUMN history TEXT NOT NULL DEFAULT 'read';
