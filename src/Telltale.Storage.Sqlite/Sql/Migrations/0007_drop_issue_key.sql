-- The issue key is no longer kept (owner decision D52): nothing reads it, and beside a person hash it is
-- the easiest way back to a person, since anyone can look up an issue's assignee. issue_id identifies the
-- issue. Dropping the column does not take the keys out of the file's free pages, nor anything else an
-- earlier version deleted there, so the store rebuilds the file once after this script (VACUUM, which cannot
-- run inside the script's transaction: SqliteStore.RebuildFileAfter).
ALTER TABLE issue DROP COLUMN issue_key;
