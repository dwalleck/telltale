# 05: Refused settings and `discover`

**What to build:** A configuration file whose `briefVisitTreatment` cannot be right is refused when it is loaded, with a message that names the file and the setting, so that the owner is never left believing figures were adjusted when nothing could be. `discover` leaves the setting alone when it rewrites the file, and writes nothing when the setting would be refused.

No message repeats what was written under the setting, a part's name included: what was typed there could be a secret pasted into the wrong place, as for every other setting. A message for an unknown part names the two parts that are allowed.

Decided at the start of the run (2026-11-17): a refusal is built in the ticket that first accepts the value it refuses, so tickets 02 and 03 already refuse an unknown part, `exclude` with no status listed, and `cycleTime` as `exclude` with no listed work-role status. This ticket builds the rest and holds all of them to one standard.

Spec: `../spec.md` ("Configuration"; user stories 5, 6, 7 and 9; "Testing Decisions", each refused configuration and the `discover` rerun). Prior art: the refused settings and `discover` ticket of the brief status visits spec.

**Blocked by:** 02, 03

**Status:** ready-for-agent

- [ ] The setting written as anything but an object is refused, a `null` included
- [ ] A part other than `timeInStatus` and `cycleTime` is refused
- [ ] A value other than `count` or `exclude` is refused, a value of the wrong type included
- [ ] A part written twice is refused
- [ ] The refusals tickets 02 and 03 built (an unknown part; `exclude` with no status listed; `cycleTime` as `exclude` with no listed work-role status) meet every rule below, and are fixed here where they do not
- [ ] Each refusal names the file and the setting, says what is allowed, and repeats no value from the file
- [ ] Each refusal is given by every command that loads the configuration
- [ ] An empty object loads and means `count` for both parts
- [ ] `discover` never writes the setting into a new file
- [ ] A `discover` rerun keeps the setting as the file has it and never changes or removes it
- [ ] A `discover` rerun on a file whose setting would be refused writes nothing, leaves the file untouched, and prints the message `run` would give, where the thresholds are checked
- [ ] Every existing test passes unchanged
- [ ] All fixtures are synthetic
