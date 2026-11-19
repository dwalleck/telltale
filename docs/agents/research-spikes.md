# Research spikes on real systems

The rules for any research ticket (`Type: research`) whose work reads Jira, GitHub, Azure DevOps or the store's real data. A ticket points here and adds only the rules that are its own.

## Access

- **Read only.** GET requests only; nothing in any system is changed.
  - Jira: the tool's token from Windows Credential Manager.
  - GitHub: `gh api --method GET --paginate`.
  - Azure DevOps: `az rest --method get --resource 499b84ac-1321-427f-aa17-267ca6975798`, or a Code (Read) token in Credential Manager.
- **Tokens** stay in Windows Credential Manager, with the narrowest read-only scope that works. A command-line tool's sign-in is fine when the tool keeps its credential in protected storage (`gh` can fall back to a plaintext file: check, and do not use that fallback). Let the tool apply the credential itself. A token is never printed, logged, passed on a command line or written to a file.
- A signed-in identity often holds more than read access, which is why GET only is the rule rather than the scope.
- The system proxy and the corporate root certificate are used as they are; TLS validation is never turned off.

## Where results go

- **The work folder on the work laptop**, outside this checkout: notes, counts and probe scripts. The handoff names it (`docs/context/handoff.md`).
- **Aggregates only**: counts per sprint, period, host or repository. No raw HTTP responses, verbose authentication logs or issue-level exports.
- Subagents never read the work folder: they get pointers to this repository only.

## People

- Pull request, commit and issue records carry names, addresses and accounts. Read IDs, commit SHAs and dates; read titles, descriptions, branch names and commit messages **in memory only**, to find keys. Write no name, address or account anywhere (ADR-0003).
- Results are per sprint, host or repository, never per person. A count of distinct authors behind a figure is fine.

## What goes in this repository

The repository is public. The ticket's answer is written in **general terms**: yes or no, the names of fields and endpoints, what each host records, and what a spec would have to decide.

- **Never in a commit, ticket, test or PR**: an issue ID or key, the project key, a pull request number, a commit SHA, a sprint or status ID, a threshold, an organization, site, repository or project name, a person, the date of a real event, or a count from real data.
- **Shape is data too.** A comparison or proportion drawn from real counts ("most", "a minority", "fewer than", "rises after") tells a reader about the real data as a number would. Say what was observed to exist or to be missing ("Jira holds branch links for some issues that have no pull request link"), not how much of it there is. The figures go to chat or to the work folder.
- Name a detailed note by its path in the work folder, so the next run can find it.

## How an answer is written

- Mark each finding by its source: *observed* on the real systems, read in a vendor's *documents*, or told by an *administrator*.
- Keep what fits no rule: count it under its reason rather than drop it, and report a host or source that was not read as "not read", never as zero.

## Probe scripts

A probe is a throwaway file-based C# app kept in the work folder, never in this checkout.

- `#:project` the CLI's project to reuse `CredentialManagerSecretStore(new WindowsCredentialVault())` for secrets.
- A `#:package Microsoft.Data.Sqlite@<version>` must name the version in `src/Telltale.Storage.Sqlite/Telltale.Storage.Sqlite.csproj`; another version fails the build with NU1605.
- Open the store with `Mode=ReadOnly`, and print aggregates only.
