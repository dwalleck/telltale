# 02: Off means every rule is off

**What to build:** With both numbers of `minimumGroupSize` at zero, nothing at all is withheld for the size of the group behind it. Every figure and count that any suppression rule (owner decisions D5 to D56) or the data quality footer withholds today is shown with its correct value, in the report and through `query`. What the setting does not touch stays as it is: person fields are hashed, nothing new is stored, and no view is grouped by person.

Ticket 01 proved one scenario. This ticket covers the rest, and fixes any view that still withholds something by another route when a minimum is zero.

Spec: `../spec.md` (owner decisions D65, D68; user stories 14 to 24, 30, 31 and 33 to 35; "The views"; "Testing Decisions").

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] Each existing suppression scenario, repeated with both numbers zero, shows the figure that was withheld, with its correct value, in the report and by `query`:
  - [ ] a point bucket with two assignees: median and P85
  - [ ] a time in status row with two assignees: median and total
  - [ ] a delivered count with two deliverers (D49), and that sprint's cycle time table with its bucket rows (D54)
  - [ ] a baseline that includes a sprint under five assignees (D50)
  - [ ] a baseline that includes a sprint whose delivered count was withheld (D56)
  - [ ] a pooled baseline row whose member row was suppressed in one baseline sprint (D44)
  - [ ] the data quality footer's two counts with too few assignees behind them
  - [ ] a footer count of nothing, shown as `0`
  - [ ] a row made only of issues with no assignee
- [ ] A report written with suppression off, over a synthetic store built to trip every rule at once, does not contain the word `suppressed` in its tables or footer
- [ ] With suppression off, neither the console nor the report names a baseline sprint as withholding the baseline
- [ ] Figures that have no data behave as they do today: a bucket with no issues has no row, and a sprint with no window is still named and skipped
- [ ] In every metric view, the `suppressed` flag is `0` in every row when suppression is off
- [ ] The existing test that no account ID, display name, email or issue summary appears in the database file or the report passes with suppression off
- [ ] The list of metric views and their columns is the same with suppression off as with the defaults, and no view has a person column
- [ ] Every existing suppression test stays as it is and passes
- [ ] Expected multi-line strings are normalized to LF
- [ ] All fixtures are synthetic
