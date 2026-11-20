# The minimum group size's numbers are defaults that a deployment may change or turn off

Amends [ADR-0003](0003-pseudonymized-aggregates-only.md). Decided by the project owner on 2026-11-12, after the first run on real data (owner decisions D64 to D69).

ADR-0003 fixed the minimum group size at five people for a team and three distinct assignees behind a figure, and built both numbers into the metric views. They are now one optional setting of the configuration file, `minimumGroupSize`, with the numbers `team` and `assigneesBehindAFigure`. When the setting is absent the numbers are five and three, and the rule applies exactly as ADR-0003 and owner decisions D5 to D56 describe it. The owner of a deployment may lower or raise either number. A number of zero turns that check off, and with both at zero no figure or count is withheld for the size of the group behind it.

The rule is still applied inside the metric views and nowhere else. `derive` records the configured numbers with the derived data, every view reads them from that one record, and `report` and `query` refuse to answer from data derived under other numbers.

## The owner's reason

- A fixed rule can leave a team without a usable baseline: one small sprint withholds every baseline figure, and a small team may have no sprint that passes.
- A rule that is strict and cannot be configured does not fit every team that might use the tool, so the numbers belong in configuration, with the strict values as the default.
- The Jira metrics are already visible to the team and the leaders involved. The tool aggregates data that anyone with access to the Jira board can already see; it adds no new information about a person.
- The metrics are there to show progress, not to blame or shame anyone.

## What this reasoning covers

It covers figures derived from Jira, which is the only source built. ADR-0003 was written with the volunteers' AI usage in mind, and that is not visible on a Jira board. A later change that adds agent sessions, GitHub data or any result grouped by people decides how the setting applies there.

## What does not change

- Person fields are pseudonymized at import with the salted hash, whatever the setting. Names, emails, issue keys and free text are still not stored.
- The tool still has no view or report grouped by person, and the setting creates none. The consented exceptions of ADR-0003 are not built and are not touched.
- ADR-0005 is unaffected.
- The success criteria and the metric definitions are unchanged. What changes is which figures are shown, and that change was made after results had been seen.

## Alternatives considered

- **An on/off switch with the numbers fixed at five and three.** Rejected: a team of four or six needs other numbers, not only "all or nothing", and a switch beside the numbers would be a second way to say what zero already says.
- **Removing the rule.** Rejected: other teams that use the tool may still need it, so it stays available and stays the default.

## Consequences

- With either number below its default, a figure for a bucket or a status that one or two people worked on in a sprint is in effect a figure about them, to anyone who knows who worked on what. A report written under such numbers says so (owner decision D67).
- Changing the setting needs a `derive` and no `backfill`: the store has always held every row, and the numbers decide only what the views show.
- Opening the database file in another program bypasses the views, as it always has.

## Addendum, 2026-11-19: pull request figures

"A later change that adds ... GitHub data ... decides how the setting applies there." The change that added pull request timing decides it as follows. This is the reading the pull request timing spec was built under, not a numbered owner decision; the owner may replace it.

- For pull request figures both checks count distinct pull request authors, as salted hashes. Jira assignees play no part, also in a sprint that has both kinds of figure.
- The team check counts the authors of the pull requests that have any pull request observation in the sprint, within the scope the figure covers (one host, or the hosts combined). A sprint that fails it shows no pull request row.
- The check of a figure counts the authors of the pull requests the figure is computed from. A row that fails only this check keeps its count.
- Counts that explain a figure (diagnostic counts) are withheld by the same two checks.
- The numbers are the same two numbers of `minimumGroupSize`, read from the rebuild's record as every view reads them. Zero turns a check off.

The owner's reason above was that the Jira figures aggregate what anyone with access to the board can already see. A repository's pull requests, their authors and their reviews are likewise visible to everyone with access to the repository, so the same default and the same setting are applied. Nothing is stored of who reviewed a pull request, so no figure can be grouped by reviewer.

In a shared repository the authors include people of other teams. The checks count them as they count anyone: the figures are of the configured repositories, and no team membership is inferred.
