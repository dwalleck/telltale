# Coding standards

Read at review, with the diff in front of you. Each rule is a judgement the build and the tests cannot make. Each has the case from this repository that taught it.

## A setting that changes over a filled store

For every setting a change adds or reads, trace what happens when its value changes after the store holds rows collected under the old value. Rows the new value no longer reaches must be refreshed, removed, marked or refused: staying as they were and still being counted is a defect.

*Case:* moving `collectFrom` forward left a pull request stored as open that had since closed, and every later sprint counted it as still waiting.

## What a broad catch swallows

For every `catch` of `UserFacingException` or wider, list the failures that reach it. A failure of the one thing asked for (the host refused this pull request, this answer has an unknown shape) may be handled there. The client giving up (retries used up, the failure budget, a rate limit, the proxy, a 401) must leave the loop.

*Case:* a per-pull-request catch in the Azure DevOps collector took the client's give-ups too, so a read went on sending requests long after its failure budget was spent and then recorded the host as read.

## A message is true on every path that prints it

For every message a change adds or reuses, follow each path that reaches it and check each claim: what was stored, what was read, what the reader should do next.

*Case:* "Nothing was stored." was printed by a path that had stored every pull request, and "Run derive first" where only a sync could help.

## A fake answers as the host answers

A fake host emits the formats the real host emits: timestamp precision, optional fields left out, header names, paging. A value the fake makes simpler than the host is a branch no test reaches.

*Case:* the fake Azure DevOps wrote times to the millisecond. The host writes seven digits, so on a real host every closed pull request compared as changed and was read again at every sync.

## A new test is seen to fail

A test for a rule fails when the rule is removed. For a test added with a fix, the review asks for the failing run. For a test of a guard that already existed, the review asks which line, when deleted, turns the test red.

*Case:* several tests in the PR timing fix passes passed before their fix was written, among them the test for the guard that keeps a token from following a link to another host.
