-- The statuses the configuration gives a role. position is board order: the order the configuration
-- lists them in, from 1. is_done is 1 for a status with the done role.
CREATE TABLE board_status (
    status_id TEXT PRIMARY KEY,
    position  INTEGER NOT NULL,
    is_done   INTEGER NOT NULL
) STRICT;
