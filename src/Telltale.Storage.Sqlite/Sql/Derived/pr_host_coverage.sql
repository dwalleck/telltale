-- What the store holds of each configured pull request host for each measure (PullRequestTiming.cs): one row
-- per configured host and measure of pr_measure, and none for a host that is not configured. position is the
-- order the hosts are configured and listed in. coverage is one of:
--   'read'             the host's last collection ended, and what the measure rests on was read: a count of 0
--                      is then a count, the host was read and has no such wait;
--   'failed read'      the host's last collection failed: its waits are those of earlier collections, if any;
--   'not read'         the host is configured and no collection of it has ever ended or failed;
--   'reviews not read' the host was read, and the reviews the measure rests on were not (the Ops group's
--                      members could not be read): the measure has no wait from this host.
-- Under anything but 'read' a missing wait is not known, and is never counted as zero.
CREATE TABLE pr_host_coverage (
    host     TEXT NOT NULL,
    position INTEGER NOT NULL,
    measure  TEXT NOT NULL,
    coverage TEXT NOT NULL,
    PRIMARY KEY (host, measure)
) STRICT;
