# 04: Refused settings, and `discover` keeps the setting

**What to build:** An owner who mistypes the `briefVisitThresholds` setting finds out when the file is loaded, from a message that names the file and the setting, and not from a footer line that never appears. An owner who has written the setting does not lose it when `discover` rewrites the file.

Spec: `../spec.md` (owner decision D58; the confirmed readings on fractions and done statuses; user stories 3 to 7 and 9; "Configuration").

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] The file is refused, with a message that names the file and the setting, when:
  - [ ] a key is not a status in `statusRoles`
  - [ ] a key is a status whose role is done
  - [ ] a threshold is zero
  - [ ] a threshold is negative
  - [ ] a threshold is a quoted number
  - [ ] a threshold is `null`, a boolean or text
- [ ] No refusal message quotes a status ID, as for refused status roles (D43)
- [ ] A fractional threshold greater than zero is accepted
- [ ] An empty dictionary loads and behaves exactly as an absent setting: no lines, no rows, an unchanged report
- [ ] A status with any role other than done may be listed
- [ ] A `discover` rerun keeps the setting as the owner wrote it
- [ ] A `discover` rerun on a file without the setting leaves it without the setting
- [ ] The first `discover` does not write the setting
- [ ] A status the owner removed from `statusRoles` while it is still listed under `briefVisitThresholds` is refused at load like any unknown status
- [ ] All fixtures are synthetic
