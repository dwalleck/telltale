# 05: Time in status table

**What to build:** The report gains the time in status table: for each status, in board order, the issue count and the median business days issues spent in it during the sprint. The collector fetches Sprint field history, derive builds membership intervals and per-issue, per-sprint status durations using the existing Core behaviour, and a metric view provides the medians.

Owner decisions (2026-11-12): board order is the order of statuses in the configuration file (D19). Done-role statuses are left out of the table (D20). An issue counts toward a status only if it spent more than zero business days there (D21). The issue's current sprint IDs are stored (D22). A status nobody was in is omitted (D23).

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] An issue added to the sprint after it started only contributes time from when it joined
- [ ] An issue removed mid-sprint only contributes time while it was a member
- [ ] An issue carried over from the previous sprint contributes to each sprint only within that sprint's window
- [ ] Statuses appear in board order, and a status no issue spent time in is omitted or shown with a zero count
- [ ] Time in Shipped is stored in status intervals
- [ ] Calendar time, business time and business days are all stored per issue, sprint and status
