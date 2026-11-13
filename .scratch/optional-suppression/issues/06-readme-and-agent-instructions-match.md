# 06: The README and the agent instructions match what is built

**What to build:** Someone reading the repository after tickets 01 to 05 finds the minimum group size described as it now works: on by default with five and three, configurable, and off at zero. The README tells an owner how to set it and what a report then shows. The agent instructions no longer say the setting is "being made configurable" or warn against weakening the views before it exists. The project handoff document points at the spec.

This ticket changes documents only. The ADR, the glossary and the owner decisions list were changed in ticket 01.

Spec: `../spec.md` ("Documents changed with the code"; user stories 13 and 38).

**Blocked by:** 02, 03, 04, 05

**Status:** ready-for-agent

- [ ] The README's "Privacy rules" section says small groups are withheld by default, how to change the two numbers or turn the rule off, and that a report written below the defaults says so
- [ ] The README's configuration example includes the setting, and the "Setting formats" table has a row for it with its format and an example
- [ ] The README's list of what is refused at load includes the refused forms of the setting
- [ ] The README says `derive` is needed after the setting changes
- [ ] The README says an existing store needs one `derive` after upgrading to this version, because the derived tables changed, and that `derive` calls no Jira (owner, 2026-11-13)
- [ ] The owner decisions list has D70 in its "Commands, configuration and secrets" section: `discover` keeps its rule of never writing a file `run` would refuse for `minimumGroupSize` too; a rerun on a file whose numbers the loader refuses stops and leaves the file untouched (owner, 2026-11-13). Its intro sentence on when decisions were made covers 2026-11-13
- [ ] The pointer to the new ADR in ADR-0003 is on the first line under the title, not above it (owner, 2026-11-13). Nothing else in ADR-0003 changes
- [ ] Each README claim about the setting was checked against the built tool, as the existing "Setting formats" claims were
- [ ] The agent instructions describe the rule as built: reporting on the team and pseudonymization unchanged, the minimum group size a configurable default that the views enforce at whatever numbers are set. The "being made configurable" and "until that is built" wording is gone
- [ ] The owner has approved the agent instructions' new wording before it is committed
- [ ] The project handoff document has one dated line pointing at the spec and saying the setting is built
- [ ] No document still says the rule cannot be configured, apart from ADR-0003, which is left as written under its pointer to the new ADR
- [ ] Nothing in the documents names a real site, project, sprint or configured value
