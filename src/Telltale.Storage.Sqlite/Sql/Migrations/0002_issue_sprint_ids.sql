-- What the issue's Sprint field lists now: comma-separated sprint IDs, the form a 'sprint' change has
-- in from_value and to_value; null when the issue is in no sprint. Membership intervals are rebuilt
-- from the field's history, and an issue with no such history has been in these sprints since it was created.
ALTER TABLE issue ADD COLUMN sprint_ids TEXT NULL;
