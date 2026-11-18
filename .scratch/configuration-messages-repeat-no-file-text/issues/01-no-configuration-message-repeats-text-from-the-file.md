# 01: No configuration message repeats text from the file

**What to build:** Whatever is wrong with the configuration file, no command prints text that was typed in it. A message names the file, the setting when the setting's name is one the tool knows, and what is allowed, in the tool's own words. The reason is the one the tool already gives for refusing unknown settings: what was typed could be a secret pasted into the wrong place (owner decision D43).

The brief visit treatment run closed three routes: a key written twice, in `discover` (D105, D108); a file that is not valid JSON, in every command (D110); and anything typed under `briefVisitTreatment`. Its last review mapped what is left. All of it is older than that feature.

## What can still be printed (found 2026-11-17, by the review of the final fix pass)

By `run`, `derive`, `report`, `query`, `backfill` and `sync`, which read the file the same way:

- An unknown key written once or twice, at the top level or under `jira`, in full, in the runtime's "could not be mapped" message. `discover` prints the same for a key written once.
- An unknown name under `minimumGroupSize`, in full: `minimumGroupSize.<name> is not a setting the tool knows.`
- A key under `statusRoles` whose value is not text, in the path the runtime gives.
- A known setting written twice, by its name, in the runtime's own words. The name is the tool's, but the message is not.

Not text from the file, but in the same place:

- `discover` ends with a stack trace for a value that is no text, such as an unpaired surrogate escape as the value of `timeZone` or of a role. No file content is printed.

Two points nobody has checked:

- What `run` says of a value, not a key, that holds bytes that are not UTF-8.
- `discover` reads the file as text, so it may never see a key with such bytes that `run` refuses.

Background: `.scratch/brief-visit-treatment/issues/06-report-explains-and-documents-match.md`, its Comments, "Final fix pass". Owner decisions D43, D74, D105, D108 and D110.

**Blocked by:** None (can start immediately).

**Status:** needs-info

Waiting on the owner to confirm the readings below. They are the author's and bind only once confirmed.

## Readings not yet confirmed

1. **An unknown setting is named by where it is, not by what it is called.** The message says that the file has a setting the tool does not know, at the top level or under a named known setting, and lists the settings allowed there. This loses the convenience of seeing a misspelled name repeated back, which is what catches a typo today.
2. **A status ID is treated as typed text.** A key under `statusRoles`, `briefVisitThresholds` or the other settings keyed by ID is never printed, as D43 already says for `briefVisitThresholds`.
3. **The runtime's own message is never passed on,** for any fault. Each fault the tool can meet has a message of the tool's own, and anything else gets one general message with the file's name.
4. **Every command and `discover` give the same message for the same single fault,** as D104 records for the brief visit settings.

## Acceptance criteria

- [ ] For each route listed above, a test writes a made-up, secret-like word in that place and compares the whole of standard output and standard error for every command and for `discover`: the word is in neither
- [ ] Each message names the file, says where the fault is in the tool's own words, and says what is allowed there
- [ ] A known setting written twice is refused in the tool's words, by every command
- [ ] A value that is no text is refused in one line by every command and by `discover`, with no stack trace
- [ ] The two unchecked points are settled by a test each
- [ ] A file the tool accepts today is still accepted, and nothing it writes or derives changes
- [ ] The README's lines on refused settings quote the new messages exactly
- [ ] The owner decisions list records the confirmed readings, and the D43 and D74 rows point to them
- [ ] Every existing test passes, apart from assertions on the text of a message that changes; the commit message lists each changed assertion
- [ ] Tests use TUnit; all fixtures are synthetic
