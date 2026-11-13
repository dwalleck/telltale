# 05: `discover` writes and keeps the setting

**What to build:** Someone setting the tool up for a team sees the minimum group size in the configuration file `discover` writes, with its default numbers, so they know the rule exists and where to change it. An owner who has changed the numbers, or removed the setting, does not have that undone when `discover` rewrites the file.

Spec: `../spec.md` (owner decision D69; user stories 3, 8 and 9; "Configuration").

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] The first `discover` writes `minimumGroupSize` with a team number of five and an assignees-behind-a-figure number of three
- [ ] The written file loads, and a report from it is identical to one from the same file without the setting
- [ ] A `discover` rerun keeps numbers the owner changed, including both at zero
- [ ] A rerun keeps the default numbers as written
- [ ] A rerun on a file without the setting leaves it without the setting
- [ ] A rerun still saves its backup of the file before rewriting it
- [ ] `discover` never refuses, warns about or comments on numbers the loader accepts, whatever they are
- [ ] A rerun on a file whose `minimumGroupSize` the loader refuses stops with the loader's message and leaves the file and its backups untouched, as for every other setting `run` would refuse (owner, 2026-11-13)
- [ ] All fixtures are synthetic
