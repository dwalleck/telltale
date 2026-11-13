# 01: A brief visit count in the footer, end to end

**What to build:** The owner lists a status and a threshold in minutes under `briefVisitThresholds` in the configuration file. After `derive`, the sprint report's data quality footer says how many of the issues behind that status's time in status row had a visit shorter than the threshold, and `query` returns the same count from a new metric view, `brief_visits_per_sprint`. No existing figure changes. This is the tracer bullet: one status in one ordinary sprint, through configuration, the derive settings, a derived table, the view and the report. Tickets 02 to 05 widen it.

The view applies the whole withholding rule (owner decision D62 and the confirmed reading on zero) from this ticket on, so that an unguarded count never exists. Ticket 03 probes it.

A new term is defined here, so the record changes in the same commit: the glossary and the owner decisions list.

Spec: `../spec.md` (owner decisions D58 to D62; the confirmed readings; "Configuration", "Deriving", "The metric view"; user stories 1 to 4, 8, 11, 12, 14, 26 and 32 to 36).

**Blocked by:** None (can start immediately).

**Status:** ready-for-agent

- [ ] A configuration file with `briefVisitThresholds` mapping a status ID to a number of minutes loads; a file without the setting loads
- [ ] In a synthetic sprint that passes the team check, with at least three assignees behind the counted issues, a catch-up sequence (a not started status, then two work statuses seconds apart) gives a footer line for the listed status with the number of issues that had a brief visit
- [ ] The count is of issues in that status's time in status row, and is never larger than the row's issue count
- [ ] `query` against `brief_visits_per_sprint` returns the same count, with the sprint, the status, its name, its board position, the threshold and a suppressed flag
- [ ] The view withholds the count when the status's time in status row is suppressed, when fewer than three distinct assignees are behind the counted issues, and when the count is zero; a sprint that fails the team check has no rows in the view
- [ ] The view reads the minimum group size's numbers from the one place every other metric view reads them
- [ ] The new derived table holds an issue ID, a sprint ID and a status ID and nothing else: no duration, no timestamp, no visit count
- [ ] The rule that decides whether a stay is brief is a pure function beside the existing interval code, with its own tests
- [ ] `derive` rebuilds the brief visit data with the rest; two derives of the same raw data and settings give the same counts
- [ ] After a threshold changes, `report` refuses and says to run `derive`; no `backfill` is needed
- [ ] With no setting, the report and every existing view's results are identical to today's, apart from the as-of time
- [ ] Brief visits write nothing to the changelog anomalies, and the footer's anomaly count is unchanged
- [ ] The glossary has a "Brief visit" entry (the definition only) and one sentence in "Minimum group size" covering the count
- [ ] The owner decisions list has D58 to D63 in a new "Data quality" section
- [ ] The commit message gives the reason for the new definition and says it was set on 2026-11-12, after the first run on real data
- [ ] Every existing test passes unchanged
- [ ] All fixtures are synthetic
