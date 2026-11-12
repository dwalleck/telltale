# 06: Suppression in the views and the report

**What to build:** Figures that could reveal one person's work are suppressed (ADR-0003). In the cycle time and time in status views, a row with fewer than three distinct assignees behind it, or any row in a sprint with fewer than five distinct assignees, has null figures and a suppressed flag. The delivered count is subject to the five-person check only. The report prints "suppressed" for such cells. The rule lives inside the views, so ad hoc SQL against them cannot bypass it. An issue counts toward its assignee at the time of the sync; unassigned issues count toward nobody.

Owner decisions (2026-11-12): a suppressed row still shows its issue count; only the figures are withheld. The five-person check counts the distinct assignees of every issue in the sprint, not only the delivered ones: issues that were members of the sprint, plus issues delivered in its window whether members or not (D36). Abandoned issues still count in the time in status table (D39).

Carried over from the ticket 01 review: the delivered count view has no guard yet, and the path the report uses to query views opens the store read-only but can still read any table. This ticket adds the guard and limits that path to the metric views.

**Blocked by:** 03, 05

**Status:** ready-for-agent

- [ ] A point bucket with two distinct assignees reads "suppressed" in the report
- [ ] A direct SQL query of the cycle time view returns null figures and a suppressed flag for that bucket
- [ ] A sprint with four distinct assignees suppresses every figure, including the delivered count
- [ ] A bucket with three distinct assignees in a sprint with five is reported
- [ ] Unassigned issues do not raise an assignee count
- [ ] The report never shows a blank or zero in place of a suppressed figure
- [ ] A suppressed row shows its issue count beside "suppressed", in the view and in the report
- [ ] A sprint with five distinct assignees across all its issues, but only four among delivered issues, is not suppressed by the five-person check
- [ ] The report's view-query path refuses to read a raw or derived table
