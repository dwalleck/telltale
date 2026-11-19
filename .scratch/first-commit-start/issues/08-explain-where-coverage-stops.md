# 08: Explain where coverage stops across sources

**What to build:** Explain the loss from all delivered issues to usable pairs, without mistaking sources not read for sources with no matches.

**Blocked by:** 04 — Keep incomplete or ineligible evidence out of the comparison; 06 — Show what a shared commit SHA establishes; 07 — Report coverage and brief-visit offsets by delivery sprint.

**Status:** ready-for-agent

- [ ] For both populations report distinct issues found per route, their deduplicated union, attribution, a PR opened by delivery and a usable timestamp. Do not sum overlapping route counts.
- [ ] Distinguish no match within readable scope, outside scope, candidate only, conflicting attribution, only post-delivery PRs, tied ambiguous first PRs, missing history and unusable dates. Distinguish a source not read or failed from a complete empty read. Never imply an unmatched board issue necessarily involved code.
- [ ] Show the full explanation on synthetic mixed-host data. Check team and per-figure suppression for totals, diagnostics and offsets through report and actual owner query. Neither surface exposes issue keys, PR numbers, SHAs or individual timelines. Do not reuse exploratory merged-by-delivery counts as product coverage.
