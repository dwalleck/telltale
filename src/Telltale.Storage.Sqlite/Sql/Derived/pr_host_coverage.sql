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
--
-- collect_from is the start date the host's stored pull requests were collected under, as the collection
-- recorded it with the raw data (pull_request_host.collect_from): a stored time, the same on each of the
-- host's rows, or null when no start date bounded them. A pull request last updated before it (on Azure
-- DevOps: closed before it) is not in the store, so an event before it may be missing. That makes coverage a
-- matter of the sprint as well: the metric views give 'partial' in place of 'read' for a host's rows of a
-- sprint that began before collect_from, and for its rows of the waits in no sprint when collect_from is set.
-- This table states the host's coverage apart from that, and the date.
CREATE TABLE pr_host_coverage (
    host         TEXT NOT NULL,
    position     INTEGER NOT NULL,
    measure      TEXT NOT NULL,
    coverage     TEXT NOT NULL,
    collect_from TEXT NULL,
    PRIMARY KEY (host, measure)
) STRICT;
