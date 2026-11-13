-- Brief visits (GLOSSARY: Brief visit): a row says that the issue has at least one brief visit to the status
-- that counts toward the sprint, that is, one that overlaps the time the sprint's time in status counts
-- (inside the sprint window, while the issue was a member). Derive decides (BriefVisits.cs); the view
-- brief_visits_per_sprint counts the rows and applies the minimum group size.
-- The table holds the three IDs and nothing else: no duration, no time, no number of visits.
CREATE TABLE brief_visit (
    issue_id  TEXT NOT NULL,
    sprint_id TEXT NOT NULL,
    status_id TEXT NOT NULL,
    PRIMARY KEY (issue_id, sprint_id, status_id)
) STRICT;
