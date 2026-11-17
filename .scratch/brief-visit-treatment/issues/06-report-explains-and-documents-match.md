# 06: The report explains, and the documents match

**What to build:** A reader of a sprint report understands what was done with brief visits and does not take it for trimming of inconvenient results, or the adjusted figure for a corrected one. The method note carries one paragraph on the treatment, printed when at least one status is listed, in place of today's sentence that nothing is excluded. Someone setting the tool up finds the setting, what each value does to each figure, and the new view and column in the README. The record is complete: every decision of the spec is in the owner decisions list.

Exact wording is the implementer's, within the report's existing style, and is fixed by tests.

Spec: `../spec.md` ("The report", method note; "Documents changed with the code"; "Further Notes"; user stories 10 and 26 to 29). The pilot plan's "Brief visits" section already records which treatment the evaluation is judged under (D96); this ticket does not change it.

**Blocked by:** 04, 05

**Status:** ready-for-agent

- [ ] The method note states the treatment in force for each of the two metrics
- [ ] It says what was left out, in the words of D84 and D85
- [ ] It says why: a stay shorter than the threshold is too short to be the work, and its usual cause is an issue moved through several statuses at once after the work was done, so its recorded length says when the board was updated and not how long the work took
- [ ] It says what leaving out does not do: the time the work took is still recorded under the earlier status, the figures describe the issues that were moved on time, which may not be typical, and no length was estimated or replaced
- [ ] It says the rule was added on 2026-11-16, after results had been seen
- [ ] It says a zero and a withheld count both read `suppressed`
- [ ] With both parts as `count`, the report is today's plus the cycle time footer line and the method note's sentence about it
- [ ] With no status listed, the report is byte for byte today's, apart from the as-of time
- [ ] The README has the setting in its example and in the "Setting formats" table, with what each value does to each figure
- [ ] The README's "What a report shows" has the new lines, its `query` section has the new view and the new column, and `derive` is listed as needed after a treatment change
- [ ] The README notes that two derives under different treatments can be compared, beside its existing note on thresholds (D79, D95)
- [ ] The README's refusal messages match the ones the tool gives
- [ ] The glossary's "Brief visit" entry agrees with the "Treatment" entry in both clauses: which row the count is of (the row before anything was left out, D93), and the sentence that says no duration is excluded or corrected because of one
- [ ] `CLAUDE.md` names D94, D95, D97 and D98 among the recorded exceptions to the minimum group size rule, beside D37, D78 and D79 (the owner consented on 2026-11-17, run decision bvt-D12); nothing else in `CLAUDE.md` changes
- [ ] The paragraph replaces the sentences tickets 02 to 04 corrected one at a time, and nothing it says is untrue under any of the four combinations of the two parts
- [ ] The owner decisions list has every one of D83 to D102, each with its source, D88 and D96 included; the D90 row ends as the spec does (a cycle whose first stay is bounded by a changelog anomaly is kept)
- [ ] No code comment, report text or document cites a decision of the implementation run by its run number (`bvt-D...`): each says the rule in words or cites the owner decision
- [ ] The comments and sentences the build order left untrue are corrected (the cross-ticket review's X10 names four: one in the report's code, one in the time in status view, one in the derive, one in the README)
- [ ] The project handoff document has one dated line pointing at the spec
- [ ] Nothing in a commit, a test or a document holds a real sprint ID, status ID, threshold or report value
- [ ] Expected multi-line strings are normalized to LF
- [ ] All fixtures are synthetic
