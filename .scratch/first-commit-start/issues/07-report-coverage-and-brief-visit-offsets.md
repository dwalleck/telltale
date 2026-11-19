# 07: Report coverage and brief-visit offsets by delivery sprint

**What to build:** Show how representative the paired comparison is, including the delivered issues with no PR evidence.

**Blocked by:** 02 — Report the first attributed GitHub PR commit.

**Status:** ready-for-agent

- [ ] Count all delivered non-Spike issues by delivery sprint with a separate between-sprints row. Exclude Spike by configured type ID from both numerator and denominator. Ordinary starts still collect evidence; unmatched issues do not disappear.
- [ ] Show all-issue and brief-start coverage. In the brief subset report contributing pairs, before/equal/after counts and median signed elapsed offset: commit author time minus original first Jira work-status entry. Do not fill missing pairs from board time.
- [ ] Exercise negative, zero and positive offsets, including a cycle omitted under the brief-visit treatment. Verify aggregate counts and median through the sprint report and actual owner query with suppression enabled and disabled. Neither existing cycle treatment changes.
