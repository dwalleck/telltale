# Local SQLite store, with derived data always rebuilt from raw

The tool runs on one developer laptop for the first 3–4 sprints (no infrastructure until there is buy-in), so the store is a single SQLite file. It has three layers: raw tables that mirror source data and are never edited after import; derived tables (status intervals, membership intervals, PR wait intervals) rebuilt from raw on every run; and metric views that reports read from. Because derived data is rebuilt, changing a definition means rerunning, never migrating.

"Mirror" means selected fields only. Every person field is hashed on the way in (ADR-0003), and no original payloads, summaries, descriptions or comments are stored. A new metric that needs an uncollected field means a refetch through the backfill.

## Considered Options

- Computing metrics once per sprint without storing history: cheap per sprint, but every definition change and every trend query would mean refetching everything.
- A shared database from day one: not available without buy-in, and unnecessary for one machine.

## Consequences

- Storage sits behind a repository layer so SQLite can be swapped for Postgres when moving to AWS.
- The database is disposable except for imported session summaries, which cannot be refetched; their shared folder is the permanent copy.
