# 12: `secrets set` and Windows Credential Manager

**What to build:** `secrets set` stores the Jira account email, the Jira API token and the hashing salt in Windows Credential Manager, behind the secret-store interface from ticket 01, and can generate the salt. On Windows the tool reads secrets from Credential Manager; elsewhere it uses environment variables. Secret values are never echoed, logged or written to a file. Carried over from the ticket 01 review: until this ticket lands, every platform reads secrets from environment variables, which `CLAUDE.md` does not allow in production on Windows. The Credential Manager implementation cannot be exercised off Windows, so its final check is by hand in ticket 13.

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] `secrets set` prompts for or accepts each secret without echoing it, and can generate a random salt
- [ ] On Windows the Credential Manager store is selected and environment variables are never read for secrets; on other systems the environment-variable store is
- [ ] A missing secret produces a message naming which one and how to set it
- [ ] No secret value appears in logs, the configuration file or the database
- [ ] The solution still builds and all tests pass on a non-Windows machine
