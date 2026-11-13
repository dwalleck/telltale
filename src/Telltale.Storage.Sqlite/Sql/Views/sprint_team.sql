-- The per-sprint checks of the minimum group size (GLOSSARY, ADR-0003), each stated here once: one row per
-- sprint that counts. Every metric view about a sprint joins this view and reads the flag that applies to it.
--
-- team_suppressed is the team check: 1 when the sprint has fewer distinct assignees than a team
-- (minimum_group_size.team: the configured number, five by default). Such a sprint shows nothing: no delivered count, and one "suppressed" row
-- in place of each table's rows (owner decisions D41, D45).
--
-- delivered_suppressed is the check of the sprint's delivered count (owner decision D49): 1 when the team
-- check fails, or fewer distinct assignees than a figure needs (minimum_group_size.assignees_behind_a_figure:
-- the configured number, three by default) are among the issues delivered in the sprint. A sprint in which nothing was delivered has nobody
-- behind its count, so it is 1 there too. What gives a withheld delivered count back is withheld with it: the
-- cycle time table's bucket rows, whose counts add up to it and whose number bounds it (owner decision D54).
-- The time-in-status table does not read this flag (owner decision D55).
--
-- An issue counts toward its assignee at the time of the sync; an unassigned issue counts toward nobody.
-- Every metric view about a sprint expands this view, the baseline views several times. That cost is accepted:
-- the store is one team's board, and the checks staying inside the views is the design (ADR-0003).
CREATE VIEW sprint_team AS
WITH sprint_issue AS (
    -- Every issue of the sprint, not only the delivered ones (owner decision D6): an issue that spent time
    -- in the sprint as a member, whatever became of it, and an issue delivered in the sprint's window,
    -- member or not, since its figures are reported under this sprint.
    SELECT t.sprint_id, t.issue_id
    FROM sprint_status_time t
    UNION
    SELECT c.delivery_sprint_id, c.issue_id
    FROM issue_cycle c
    WHERE c.delivered = 1
),
counted AS (
    SELECT w.sprint_id,
           COUNT(DISTINCT i.assignee_hash) AS assignees,
           COUNT(DISTINCT CASE WHEN c.issue_id IS NOT NULL THEN i.assignee_hash END) AS delivering_assignees
    FROM sprint_window w
    LEFT JOIN sprint_issue si ON si.sprint_id = w.sprint_id
    LEFT JOIN issue i ON i.issue_id = si.issue_id
    LEFT JOIN issue_cycle c ON c.issue_id = si.issue_id AND c.delivered = 1 AND c.delivery_sprint_id = w.sprint_id
    GROUP BY w.sprint_id
)
SELECT n.sprint_id,
       CASE WHEN n.assignees >= g.team THEN 0 ELSE 1 END AS team_suppressed,
       CASE WHEN n.assignees >= g.team AND n.delivering_assignees >= g.assignees_behind_a_figure THEN 0 ELSE 1 END AS delivered_suppressed
FROM counted n, minimum_group_size g;
