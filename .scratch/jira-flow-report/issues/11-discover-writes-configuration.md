# 11: `discover` writes the configuration

**What to build:** `discover` reads a Jira site and writes the configuration file so the owner does not look up IDs by hand. It first makes one call to check proxy and certificate trust and, on a TLS or proxy failure, says plainly that this is the likely cause. It then lists the board's statuses with their Jira category and a proposed role (not started, work, blocked, done), lists resolutions so the owner can mark the abandoned ones, finds the Sprint and story point field IDs, and writes these with the site URL and board ID to the configuration file. Proposed roles and abandoned resolutions are meant to be reviewed and edited.

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] Against the fake Jira, `discover` writes a configuration file that `run` then accepts unchanged
- [ ] Statuses are listed with name, ID, Jira category and proposed role
- [ ] Resolutions are listed with name and ID
- [ ] The Sprint and story point field IDs are found from the field listing
- [ ] A simulated certificate or proxy failure produces a message naming proxy and root-certificate trust as the likely cause
- [ ] The written file contains no secrets
- [ ] Rerunning `discover` does not silently discard roles or resolutions the owner has edited
