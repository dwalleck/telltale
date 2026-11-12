-- The issue key is no longer kept (owner decision D52): nothing reads it, and beside a person hash it is
-- the easiest way back to a person, since anyone can look up an issue's assignee. issue_id identifies the
-- issue.
ALTER TABLE issue DROP COLUMN issue_key;
