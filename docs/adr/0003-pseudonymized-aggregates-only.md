# Identities are pseudonymized at import, and only aggregates of five or more people are reported

The data could easily become individual surveillance, which would destroy the trust of the volunteers whose honest usage the evaluation depends on. People are identified only by a salted hash computed at import; the salt lives in the secret store, never in the database, and no name-to-hash table is kept. The minimum group size is enforced inside the metric views, so no query path (including ad hoc SQL) can produce a result grouped by people that covers fewer than five. The tool is built so it cannot produce a per-person report, which is the answer when leadership eventually asks for one.

## Results grouped by issue attributes

The team is six developers. A result grouped by issue attributes (sprint, point bucket) rarely has five distinct assignees behind it, so applying the five-person minimum to every cell would blank most of the report, including the headline P85 cycle time by point bucket. Such a result describes a kind of work, not a person, unless one person did nearly all of it. So it is reported when the team behind it has at least five people and the cell itself has at least three distinct assignees. The five-person minimum still applies to anything grouped by people: a cohort, a seniority level, a plan tier.

## Consented exceptions

Each is deliberate, labeled as an exception wherever it appears, and has its own views.

- **The two case studies**: the project owner and one consenting mid-level developer, each reported individually.
- **The volunteer cohort**: about three volunteers, so structured vs. ad hoc AI use is in effect a result about them. With their consent it is reported for the cohort as a whole, never per person. Without that consent, or two more volunteers, the evaluation's central comparison could not be published.

The other developers are not a reportable group. Their side of the comparison is the whole-team trend.

## Consequences

- Issue keys never appear next to results grouped by person or by group, since anyone can look up a ticket's assignee.
- Participants are told exactly what is collected, and the volunteer cohort's consent is obtained, before the evaluation starts.
- The plan's "compared with everyone else's change" becomes "compared with the whole-team trend".
