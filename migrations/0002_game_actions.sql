-- One row per player action, kept for gameplay research. Rows are ordered by (client_game_id, seq) and uploaded
-- in batches, so (user_id, client_game_id, seq) makes every upload safe to retry.
-- Hold state is not stored: replay the Hold/Unhold presses (and their timestamps) instead, so a die that is
-- held and then released before a roll shows up as the player changing their mind.
--   Roll:          dice = values after the roll.
--   Hold/Unhold:   dice = values at the time of the press, die_id = 1..5.
--   Score/Scratch: dice = the dice that were scored, game_index = card 0..5, category, points
--                  (0 for a scratch), yahtzee_bonus = 1 when the 100-point bonus was awarded.
--   Undo:          dice = values after the last score was taken back.
CREATE TABLE game_actions (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id         TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    client_game_id  TEXT NOT NULL,
    seq             INTEGER NOT NULL,
    mode            TEXT NOT NULL CHECK (mode IN ('Single', 'FullSheet')),
    action          TEXT NOT NULL CHECK (action IN ('Roll', 'Hold', 'Unhold', 'Score', 'Scratch', 'Undo')),
    occurred_at     INTEGER NOT NULL,
    received_at     INTEGER NOT NULL,
    turn            INTEGER NOT NULL,
    roll_number     INTEGER NOT NULL CHECK (roll_number BETWEEN 0 AND 3),
    dice            TEXT NOT NULL,
    die_id          INTEGER,
    game_index      INTEGER,
    category        TEXT,
    points          INTEGER,
    yahtzee_bonus   INTEGER NOT NULL DEFAULT 0,
    UNIQUE (user_id, client_game_id, seq)
);
CREATE INDEX idx_game_actions_user_received ON game_actions(user_id, received_at);
