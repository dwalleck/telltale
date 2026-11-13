-- Sprint changes written before the collector normalised them hold the value as Jira wrote it ("41, 42").
-- A change is never written twice, so they would stay that way. This rewrites them in the stored form of
-- sprint IDs, "41,42", the form issue.sprint_ids has. Those values were only ever sprint IDs, commas and
-- white space, so taking the white space out is the whole change: the space Jira writes after a comma,
-- and a tab, line feed, carriage return or no-break space in case one was ever stored.
-- Numbered 0005 because ticket 06 holds 0004. Scripts are recorded by name, so the order of the two does not matter.
UPDATE change
SET from_value = replace(replace(replace(replace(replace(from_value, ' ', ''), char(9), ''), char(10), ''), char(13), ''), char(160), ''),
    to_value   = replace(replace(replace(replace(replace(to_value,   ' ', ''), char(9), ''), char(10), ''), char(13), ''), char(160), '')
WHERE field = 'sprint';
