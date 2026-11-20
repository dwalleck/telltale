-- The start date the stored pull requests of a host were collected under (the host's collectFrom setting), as
-- the collection that last read the host's list recorded it: a stored time, or null when no start date bounded
-- what was collected. A pull request last updated before it (on Azure DevOps: closed before it) may be missing
-- from the store, so a sprint that began before it is not covered in full (pr_host_coverage). It is recorded with
-- the raw data and not read from the configuration, because moving the setting back changes what the store
-- holds only once a collection has read the whole list again. Rows written before this column existed say null:
-- the next collection of the host records the date.
ALTER TABLE pull_request_host ADD COLUMN collect_from TEXT NULL;
