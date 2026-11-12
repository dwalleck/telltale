# 02: Only real deliveries count

**What to build:** Abandoned issues, epics and sub-tasks stop counting as delivered. An issue in a done-role status whose resolution is in the configured abandoned list is not delivered. Epics and sub-tasks are left out of collection or of every metric, so they appear in no figure.

Owner decisions (2026-11-12): `abandonedResolutionIds` is a required setting and may be empty (D12). Epics, sub-tasks and types above epic level are left out at collection, and when a sync sees that a stored issue has become one of them it deletes that issue's rows (D13, D14). An issue type with neither a hierarchy level nor a sub-task flag stops the run (D15).

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] An issue that is Done with an abandoned resolution is not in the delivered count
- [ ] An issue that is Done with any other resolution, or none, is in the delivered count
- [ ] Epics and sub-tasks in the fixtures change no figure in the report or the views
- [ ] The abandoned resolution IDs come from configuration
