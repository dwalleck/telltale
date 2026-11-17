# 05: Refused settings and `discover`

**What to build:** A configuration file whose `briefVisitTreatment` cannot be right is refused when it is loaded, with a message that names the file and the setting, so that the owner is never left believing figures were adjusted when nothing could be. `discover` leaves the setting alone when it rewrites the file, and writes nothing when the setting would be refused.

No message repeats what was written under the setting: a value there could be a secret pasted into the wrong place, as for every other setting.

Spec: `../spec.md` ("Configuration"; user stories 5, 6, 7 and 9; "Testing Decisions", each refused configuration and the `discover` rerun). Prior art: the refused settings and `discover` ticket of the brief status visits spec.

**Blocked by:** 02, 03

**Status:** ready-for-agent

- [ ] The setting written as anything but an object is refused, a `null` included
- [ ] A part other than `timeInStatus` and `cycleTime` is refused
- [ ] A value other than `count` or `exclude` is refused, a value of the wrong type included
- [ ] A part written twice is refused
- [ ] Either part as `exclude` with no status listed in `briefVisitThresholds` is refused
- [ ] `cycleTime` as `exclude` with no listed status that has the work role is refused
- [ ] Each refusal names the file and the setting, says what is allowed, and repeats no value from the file
- [ ] Each refusal is given by every command that loads the configuration
- [ ] An empty object loads and means `count` for both parts
- [ ] `discover` never writes the setting into a new file
- [ ] A `discover` rerun keeps the setting as the file has it and never changes or removes it
- [ ] A `discover` rerun on a file whose setting would be refused writes nothing, leaves the file untouched, and prints the message `run` would give, where the thresholds are checked
- [ ] Every existing test passes unchanged
- [ ] All fixtures are synthetic
