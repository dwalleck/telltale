-- When a past closing of a pull request ended, exactly: the time the host gives for the reopening that
-- followed it. reopened_by stays what it was, the time of the collection that found the closing gone, which
-- is only a moment the reopening came before. A review made after the real reopening lies before that
-- collection too, so reopened_by alone cannot tell a review made while the pull request was closed from one
-- made after it was opened again; this time can. GitHub gives it in the pull request's issue events (closed,
-- reopened), which a read asks for once, when it finds a stored pull request open again. Null where no such
-- time was read: on Azure DevOps always, on GitHub when the issue events could not be read, and in rows
-- written before this column existed. Then the closing is as the collections saw it and no more is known.
ALTER TABLE pull_request_past_closure ADD COLUMN reopened TEXT NULL;
