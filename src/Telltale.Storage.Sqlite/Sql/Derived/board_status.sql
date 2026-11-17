-- The statuses the configuration gives a role. position is board order: the order the configuration
-- lists them in, from 1. is_done is 1 for a status with the done role, and is_work is 1 for a status with
-- the work role: the one place a view learns a status's role from.
CREATE TABLE board_status (
    status_id TEXT PRIMARY KEY,
    position  INTEGER NOT NULL,
    is_done   INTEGER NOT NULL,
    is_work   INTEGER NOT NULL
) STRICT;
