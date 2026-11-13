# 01: Suppression can be turned off, end to end

**What to build:** The owner adds the `minimumGroupSize` setting to the configuration file with a team number and an assignees-behind-a-figure number. With both at zero, a sprint that the minimum group size rule withheld is reported in full, in the sprint report and through `query`. With the setting absent the numbers are five and three and nothing changes. This is the tracer bullet: one scenario through configuration, the derive settings, the metric views and the report. Tickets 02 to 05 widen it.

The rule's definition changes here, so the record changes in the same commit: the new ADR, the glossary and the owner decisions list.

Spec: `../spec.md` (owner decisions D64, D65, D66, D68; "Configuration", "The views", "Documents changed with the code", and the owner's reason under "Further Notes").

**Blocked by:** None (can start immediately).

**Status:** ready-for-agent

- [ ] A configuration file with `minimumGroupSize` giving two whole numbers of zero or more loads; a file without the setting loads and behaves as five and three
- [ ] With both numbers zero, a synthetic sprint of four assignees shows its delivered count, its cycle time rows and its time in status rows, with figures, in the report
- [ ] The same figures are returned by `query` against the metric views, and every view keeps the columns it has today
- [ ] The two numbers are stated in one place that every metric view reads; no view states a number of its own
- [ ] The numbers are recorded with the other derive settings: after the setting changes in either direction, `report` refuses and says to run `derive`, and no `backfill` is needed
- [ ] With the setting absent, and with it written as five and three, the report and the `query` results for that sprint are identical to today's, apart from the as-of time
- [ ] Every existing test passes unchanged, including the checks that no query recovers, bounds or tells zero from non-zero for a withheld delivered count
- [ ] A new ADR amends ADR-0003: the numbers are defaults a deployment may change or set to zero; pseudonymization and the absence of per-person reports are unchanged; it gives the date, the alternatives considered, the owner's reason as the spec records it, and says the reasoning covers Jira-derived figures. ADR-0003 gains a first line pointing to it and is otherwise unchanged
- [ ] The glossary's "Minimum group size" entry says five and three are defaults and that configuration may set other numbers, zero turning a check off
- [ ] The owner decisions list has D64 to D69 in its "Suppression" section, with a sentence that the earlier decisions describe the rule at its default numbers and apply at whatever numbers are configured
- [ ] The commit message gives the reason for the definition change and says it was made on 2026-11-12, after the first run on real data
- [ ] All fixtures are synthetic
