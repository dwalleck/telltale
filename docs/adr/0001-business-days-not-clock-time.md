# Durations are measured in business days, not clock time or working hours

Other teams report 24-hour clock time, which mostly measures when work was opened (a PR opened Friday afternoon shows ~64 hours to review) and buries real change in weekend noise. Working hours would need per-person schedules, time zones and PTO that neither Jira nor GitHub knows. We exclude weekends and company holidays and count every other day in full, in the team's time zone. Calendar time is still computed and shown alongside, so our numbers can be compared with other teams' and nobody can say we inflated results by changing the definition.

## Consequences

- Forecasts are simulated in business days and converted back to calendar dates for stakeholders.
- A company holiday calendar is required configuration.
