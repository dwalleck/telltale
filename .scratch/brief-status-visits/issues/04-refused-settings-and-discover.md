# 04: Refused settings, and `discover` keeps the setting

**What to build:** An owner who mistypes the `briefVisitThresholds` setting finds out when the file is loaded, from a message that names the file and the setting, and not from a footer line that never appears. An owner who has written the setting does not lose it when `discover` rewrites the file.

Spec: `../spec.md` (owner decision D58; the confirmed readings on fractions and done statuses; user stories 3 to 7 and 9; "Configuration").

**Blocked by:** 01

**Status:** resolved

- [x] The file is refused, with a message that names the file and the setting, when:
  - [x] a key is not a status in `statusRoles`
  - [x] a key is a status whose role is done
  - [x] a threshold is zero
  - [x] a threshold is negative
  - [x] a threshold is a quoted number
  - [x] a threshold is `null`, a boolean or text
- [x] No refusal message quotes a status ID, as for refused status roles (D43)
- [x] A fractional threshold greater than zero is accepted
- [x] An empty dictionary loads and behaves exactly as an absent setting: no lines, no rows, an unchanged report
- [x] A status with any role other than done may be listed
- [x] A `discover` rerun keeps the setting as the owner wrote it
- [x] A `discover` rerun on a file without the setting leaves it without the setting
- [x] The first `discover` does not write the setting
- [x] A status the owner removed from `statusRoles` while it is still listed under `briefVisitThresholds` is refused at load like any unknown status
- [x] All fixtures are synthetic

## Comments

Resolved 2026-11-13. Merged into `spec/brief-status-visits` as 2bf4301 (source `ticket/bv-04-refused-settings-and-discover` at 926b0ab; commits d4a80ea, d7e8e34, ba5eaab, f8186eb, 90717fe, 926b0ab).

- Checks: the merged tree equals the source tree, so the implementer's full check on 926b0ab stands (record `bv04-r1-record.json`, base c1d8b72): Core 136 and Pipeline 869 passed, 0 failed, 0 skipped, 0 warnings, format and line endings pass. Line endings were checked again at merge (18 files).
- Mutations: 8 new records under `.scratch/brief-status-visits/mutations/`, 04-m1 to 04-m8, all active and red. 04-m1 to 04-m7 were red at d4a80ea and again at ba5eaab, after the review fixes. 04-m8 (a threshold cut off to whole steps, not taken to the nearest) survived at ba5eaab, because no case fell under a whole number of steps; the cases 0.69, 1.13 and 4.77 were added and it is red at 90717fe, 3 of 24. Re-pointed to moved lines, the mutation unchanged, each red at ba5eaab: 01-m10 (twice: the record is made from the checked lengths of time), 04-m2 (review fix F7) and 04-m6 (review fix F4). None rerun at merge, because the merge changed no file relative to the source. The implementer records `-ValidateOnly` passing for all three features at 926b0ab. Ticket 02's record 02-m7 finds the `TimeSpan.FromMinutes` line this ticket replaced; it is re-pointed when ticket 02 is merged.
- Review (root's `code-review`, eight findings, each with a disposition from root). Fixed here, each checked against the diff by the merger: F1's comment (`RequireBriefVisitThresholds` says a status listed twice stops `discover` earlier, in its own reader, with that reader's message), F3 (the converter is bound to the one property of `TelltaleConfig` through a type-info modifier, not to the dictionary type), F4 (one form: `CheckedBriefVisitThresholds` returns lengths of time, and the rebuild's record is made from them), F5 (`RequireSetting<T>`, `IsRefusal` and `NotValid` in `ConfigFile`; messages unchanged), F6 (the converter's `Write` throws `NotSupportedException`; `ConfigFile.Written` serialises only the minimum group size, and the discover tests pass with it throwing), F7 (one guard, one walk over the listed statuses), F8 (the comment on `TelltaleConfig.BriefVisitThresholds` names the rules). No change: F2, by bv-D3. F1's behaviour is left by bv-D4.
- Decisions that touched the ticket (user, 2026-11-13). bv-D2: the five refusals beyond the spec are accepted: a threshold too large for a length of time, one that rounds to no time at all, the setting written as null or any non-object, a status listed twice, and a threshold written as a list or an object. bv-D3: on a `discover` rerun of a file with `briefVisitThresholds` and no `statusRoles`, the thresholds are checked before the Jira call and the listed statuses after it, and nothing is written if they are refused; accepted as built. bv-D4: `discover`'s own reader quotes a key written twice (a status ID under `briefVisitThresholds` or `statusRoles`); left as it is, a follow-up outside this run. The box "No refusal message quotes a status ID" is ticked on that footing: no message of this ticket's rules or reader quotes a status ID or a value, and when the file is loaded a status listed twice gets "briefVisitThresholds lists a status twice."
- The implementer's readings from the review fixes, for the user: (1) rounding: a threshold is taken to the nearest step of time (a ten-millionth of a second), not cut off, so that a threshold of up to two decimals is recorded and printed as written; (2) under one step: a threshold under one whole step is still refused, as bv-D2 accepted, and is not rounded up to one step; from one step up, a threshold finer than a step is applied, recorded and printed as its whole steps; (3) which message comes first: when a file lists both a status without a role and a done status, the message is for whichever is listed first.
- Existing assertions changed: none. All four test files are new, and no existing test file is in the diff.
- Merger's read of c1d8b72..926b0ab: the seven messages name the setting and state no status ID and no value; tests paste a synthetic token as a status ID and as a threshold and compare the whole of standard error. Every refusal is in the ticket or in bv-D2, with two that exist only for a configuration built in code and cannot be written in a file: a null dictionary ("briefVisitThresholds must not be null.") and a threshold that is not a number, which gets the message for zero and less. `discover` never writes, changes or drops the setting: it carries over the file's text, the converter's `Write` throws, and tests pin the first discover (no setting written), a rerun with the setting (kept character for character, numbers in other forms included), a rerun without it, a rerun with nothing to change (file not rewritten), and a refused rerun (no Jira request, no output, file and backups untouched). Fixtures are synthetic (`FAKE-` keys, the harness's workflow IDs, `example.invalid`). No project, package or toolkit file changed, and no stray file. `test-anti-patterns` on the new tests (30 methods: 9 core, 21 pipeline): no critical, high or medium finding; low, one: a missing space in a comment in `RefusedBriefVisitThresholdsTests` ("loaded:the").
- With the user, not settled by this merge: the three readings above; and that the record of the refusals beyond the spec goes into `docs/decisions.md` and the README with ticket 05 (review F8).
