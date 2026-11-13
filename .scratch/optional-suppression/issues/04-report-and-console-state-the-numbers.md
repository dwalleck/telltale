# 04: The report and the console state the numbers in force

**What to build:** A reader of a sprint report can tell which minimum group size it was written under. A report written with either number below its default says so in a line directly under its heading, so that a forwarded report carries the warning with it. The method note gives the numbers in force, and says nothing was withheld for group size when both are zero. The console message that names a short baseline sprint, and the `query` help text, describe the configured rule and not a fixed five and three.

A report written with the default numbers is byte for byte what it is today.

Spec: `../spec.md` (owner decision D67; user stories 25 to 29; "The report and the console").

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] With both numbers zero, the report has one line directly under its heading saying small-group suppression is off and that a figure may rest on the work of one or two people
- [ ] With either number below its default but not both zero, that line gives the two numbers in force
- [ ] With both numbers at or above the defaults, there is no such line
- [ ] The method note states the numbers in force wherever it states five and three today
- [ ] With both numbers zero, the method note's minimum group size paragraph, including its sentences on bots counting as people (D42) and on a late status's count approximating a withheld count (D55), is replaced by a statement that no figure was withheld for group size and that person fields are still hashed
- [ ] With one check at zero and the other not, the method note describes only the check that applies
- [ ] The console and report message that names a baseline sprint withholding the baseline (D50, D56) gives the configured numbers
- [ ] `query --help` says the metric views withhold small-group figures according to the `minimumGroupSize` setting, and keeps its sentence that opening the database file elsewhere bypasses the views
- [ ] With the setting absent, and with it written as five and three, the report, the console output and the method note are identical to today's, apart from the as-of time
- [ ] The wording of the new line and the method note text is fixed by tests
- [ ] Expected multi-line strings are normalized to LF
- [ ] All fixtures are synthetic
