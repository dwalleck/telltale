-- What the store was last filled from, one setting to a column, in place of the one sentence in covers that
-- every rule had to take apart again: the Jira site, the board, the project keys (in order, separated by
-- commas), the start date (an ISO date) and the IDs of the fields whose history was fetched (separated by
-- commas). A row written before these columns existed keeps their empty defaults, which equal no configured
-- setting: every command but a backfill refuses such a store.
ALTER TABLE watermark ADD COLUMN site_url TEXT NOT NULL DEFAULT '';
ALTER TABLE watermark ADD COLUMN board_id INTEGER NOT NULL DEFAULT 0;
ALTER TABLE watermark ADD COLUMN project_keys TEXT NOT NULL DEFAULT '';
ALTER TABLE watermark ADD COLUMN start_date TEXT NULL;
ALTER TABLE watermark ADD COLUMN history_fields TEXT NOT NULL DEFAULT '';
ALTER TABLE watermark DROP COLUMN covers;
