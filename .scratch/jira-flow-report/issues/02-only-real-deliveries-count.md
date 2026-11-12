# 02: Only real deliveries count

**What to build:** Abandoned issues, epics and sub-tasks stop counting as delivered. An issue in a done-role status whose resolution is in the configured abandoned list is not delivered. Epics and sub-tasks are left out of collection or of every metric, so they appear in no figure.

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] An issue that is Done with an abandoned resolution is not in the delivered count
- [ ] An issue that is Done with any other resolution, or none, is in the delivered count
- [ ] Epics and sub-tasks in the fixtures change no figure in the report or the views
- [ ] The abandoned resolution IDs come from configuration
