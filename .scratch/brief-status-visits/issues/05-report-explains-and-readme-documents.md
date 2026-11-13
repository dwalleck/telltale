# 05: The report explains brief visits, and the README documents the setting

**What to build:** A reader of a sprint report understands the brief visit lines without opening the configuration file: each names its status and its threshold, they come in board order, and the method note says what a brief visit is, that nothing was excluded or corrected because of one, and that the rule was added after the first run on real data. Someone setting the tool up can write the setting from the README alone. Brief visits never look like changelog anomalies, on the console or in the footer.

Spec: `../spec.md` ("The report", "Documents changed with the code"; user stories 10 to 13, 23 to 26 and 35).

**Blocked by:** 02, 03, 04

**Status:** ready-for-agent

- [ ] Each footer line names the status and states its threshold in minutes
- [ ] The lines come after the existing footer lines, in board order, the order of the time in status table
- [ ] A shown count and a withheld count each have fixed wording, in the report's existing style
- [ ] The method note has a paragraph on brief visits only when at least one status is listed. It says: a brief visit is one unbroken stay shorter than the status's threshold; it is measured in clock time; it is counted and no figure is excluded or corrected; a zero and a withheld count both read `suppressed`; the rule was added on 2026-11-12 after the first run on real data
- [ ] With no status listed, the report has no brief visit lines and no extra method note text, and is identical to today's apart from the as-of time
- [ ] A baseline sprint's counts appear when `report --sprint` is run on that sprint; the reported sprint's baseline columns gain nothing
- [ ] The console output of `derive` and `run` is the same with and without brief visits in the data
- [ ] The footer's changelog anomaly count is the same with and without brief visits in the data
- [ ] The README's configuration example and "Setting formats" table include the setting, with its format and an example
- [ ] The README's list of what is refused at load includes the refused forms of the setting
- [ ] The README's "What a report shows" mentions the footer line, the `query` section lists the new view, and `derive` is listed as needed after a threshold change
- [ ] Each README claim about the setting was checked against the built tool
- [ ] The project handoff document has one dated line pointing at the spec
- [ ] The wording of the lines and the method note paragraph is fixed by tests, with expected multi-line strings normalized to LF
- [ ] Nothing in the documents or fixtures names a real site, status ID, sprint or configured threshold
