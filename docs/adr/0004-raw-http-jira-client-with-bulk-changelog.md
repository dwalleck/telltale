# Jira is called with a plain HTTP client, and changelogs come from the bulk changelog endpoint

No .NET Jira SDK is mature for changelogs: Atlassian.SDK stalled and its forks target Server, and SDK helpers commonly use `expand=changelog`, which silently returns only the 100 most recent history entries with no way to page past them. We call `POST /rest/api/3/changelog/bulkfetch` directly (up to 1000 issues per call, filtered to the status and Sprint fields), and fall back to `GET /issue/{key}/changelog` per issue if needed. Never use `expand=changelog` for history.

## Consequences

- Bulkfetch is Jira Cloud only.
- Bulkfetch can return duplicate entries depending on page size, so history is deduplicated by history ID before use.
