# Triage Labels

The skills speak in terms of five canonical triage roles. This file maps those roles to the actual label strings used in this repo's issue tracker.

| Label in mattpocock/skills | Label in our tracker | Meaning                                  |
| -------------------------- | -------------------- | ---------------------------------------- |
| `needs-triage`             | `needs-triage`       | Maintainer needs to evaluate this issue  |
| `needs-info`               | `needs-info`         | Waiting on reporter for more information |
| `ready-for-agent`          | `ready-for-agent`    | Fully specified, ready for an AFK agent  |
| `ready-for-human`          | `ready-for-human`    | Requires human implementation            |
| `wontfix`                  | `wontfix`            | Will not be actioned                     |

When a skill mentions a role (e.g. "apply the AFK-ready triage label"), use the corresponding label string from this table.

Edit the "Label in our tracker" column to match whatever vocabulary you actually use.

## When a spec is `ready-for-agent`

A spec that adds a source of data (a collector, a host, a new kind of raw fact) is ready when its text answers these five questions. Each was decided mid-build in the PR timing spec, and two of the decisions were reversed after code had been written on them.

1. **Cost.** How many requests does a first read make, and what bounds it? What does a later sync skip?
2. **Backfill.** What does `backfill` do to the new rows: replace, refresh or leave?
3. **A read that stops.** What is stored when a read fails part-way, and how does the next read continue?
4. **A changed setting.** For each new setting, what happens when it changes over a store that is already filled?
5. **Minimum group size.** Does it apply to the new figures? ADR-0008 leaves this to each change that adds a source; D114 is the answer for pull request figures only.

A spec that leaves one open gets `needs-info`, with the question.
