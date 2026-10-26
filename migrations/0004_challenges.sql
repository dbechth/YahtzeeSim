-- Challenges: a creator invites players (by nickname or with an invite link) to play one game each before a deadline.
-- Entries are normal games (they are also in `scores`); a challenge only links them. Only a challenge's players can
-- read it (enforced in functions/_lib/challenges.ts).
CREATE TABLE challenges (
    id            TEXT PRIMARY KEY,
    creator_id    TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    title         TEXT NOT NULL,
    mode          TEXT NOT NULL CHECK (mode IN ('Single', 'FullSheet')),
    created_at    INTEGER NOT NULL,
    deadline      INTEGER NOT NULL,
    -- Random secret in the invite link. Shown to the challenge's players so any of them can share it.
    invite_token  TEXT NOT NULL UNIQUE,
    -- Bumped whenever something players should see happens (join, entry, finish); drives the unread dot.
    updated_at    INTEGER NOT NULL,
    -- Set when every player has an entry or the deadline has passed and results were sent.
    finished_at   INTEGER
);
CREATE INDEX idx_challenges_open ON challenges(finished_at, deadline);
CREATE INDEX idx_challenges_creator ON challenges(creator_id, created_at);

CREATE TABLE challenge_players (
    challenge_id  TEXT NOT NULL REFERENCES challenges(id) ON DELETE CASCADE,
    user_id       TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    joined_at     INTEGER NOT NULL,
    left_at       INTEGER,
    seen_at       INTEGER NOT NULL DEFAULT 0,
    reminded_at   INTEGER,
    PRIMARY KEY (challenge_id, user_id)
);
CREATE INDEX idx_challenge_players_user ON challenge_players(user_id);

CREATE TABLE challenge_entries (
    challenge_id    TEXT NOT NULL REFERENCES challenges(id) ON DELETE CASCADE,
    user_id         TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    client_game_id  TEXT NOT NULL,
    total           INTEGER NOT NULL,
    finished_at     INTEGER NOT NULL,
    PRIMARY KEY (challenge_id, user_id)
);
