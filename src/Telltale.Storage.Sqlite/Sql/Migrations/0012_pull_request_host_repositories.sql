-- The repositories the last read of a host that ended went through: a JSON array of repository names as the
-- host's collector names them (pull_request.repository), in the order they were configured then. A read that
-- failed leaves it as it was. A repository that is configured now and is not in it was added since, and no
-- read has gone through its list: derive then gives the host 'repository not read' in place of 'read'
-- (pr_host_coverage), so that what the store lacks of it is not counted as zero. Null in rows written before
-- this column existed: which repositories those reads covered is not known, and nothing is concluded from it;
-- the next collection of the host records the list.
ALTER TABLE pull_request_host ADD COLUMN repositories TEXT NULL;
