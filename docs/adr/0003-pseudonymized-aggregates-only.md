# Identities are pseudonymized at import, and only aggregates of five or more people are reported

The data could easily become individual surveillance, which would destroy the trust of the volunteers whose honest usage the pilot depends on. People are identified only by a salted hash computed at import; the salt lives in the secret store, never in the database, and no name-to-hash table is kept. The minimum group size is enforced inside the metric views, so no query path (including ad hoc SQL) can produce a result covering fewer than five people. The tool is built so it cannot produce a per-person report, which is the answer when leadership eventually asks for one.

## Consequences

- Issue keys never appear next to results grouped by person or by group, since anyone can look up a ticket's assignee.
- The two case studies (the project owner and one consenting mid-level developer) are a deliberate, labeled exception in their own views.
- Participants are told exactly what is collected before the pilot starts.
