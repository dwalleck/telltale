-- The sprints the baseline is drawn from (GLOSSARY: Baseline): the configuration's baselineSprintIds, as of
-- this rebuild. Empty when no baseline is configured. The baseline views read it and never the configuration.
CREATE TABLE baseline_sprint (
    sprint_id TEXT PRIMARY KEY
) STRICT;
