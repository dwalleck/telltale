# 04: Keep incomplete or ineligible evidence out of the comparison

**What to build:** Preserve known PR evidence while withholding an issue-level comparison when its first timestamp cannot be established.

**Blocked by:** 02 — Report the first attributed GitHub PR commit.

**Status:** ready-for-agent

- [ ] A truncated, unreadable or otherwise unusable first history leaves the issue in coverage but produces no paired offset. A readable later PR shows work observed by a known date, not the missing start.
- [ ] Among PRs opened by delivery, choose the earliest opening regardless of whether the PR is open, abandoned or merged later. Opening exactly at delivery qualifies; later opening never qualifies, even with older commits.
- [ ] Tied earliest openings count once only if every tied history is complete and agrees on the earliest author instant. Otherwise report first PR ambiguous without choosing by PR number.
- [ ] Preserve and flag pre-creation timestamps; keep a post-delivery author date as evidence only. Equality with delivery is not after delivery. Exercise each outcome through synthetic CLI reports and protected views.
