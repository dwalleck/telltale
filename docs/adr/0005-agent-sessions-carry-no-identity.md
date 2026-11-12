# Agent session summaries carry no personal identity

Session logs live on each participant's machine and reach the store through an exporter and a shared folder. Sessions do not need an identity: they link to work through the issue key in the branch name, and the person comes from Jira and GitHub (assignee, PR author), hashed at import. So the exporter writes only session ID, times, branch, model, skills invoked and tokens where available: no name, no prompts, no code. This keeps personal data and intellectual property out of transit and out of the store, and avoids sharing a hashing salt with participants.

## Consequences

- The session summary format is versioned from day one, because old exports must keep importing.
- Sessions with no issue key are non-ticket sessions, reported separately and never counted as waste.
