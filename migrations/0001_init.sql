-- Accounts are keyed by the identity provider's stable user id; no email or profile data is stored.
CREATE TABLE users (
    id            TEXT PRIMARY KEY,
    provider      TEXT NOT NULL,
    provider_sub  TEXT NOT NULL,
    nickname      TEXT COLLATE NOCASE UNIQUE,
    created_at    INTEGER NOT NULL,
    UNIQUE (provider, provider_sub)
);

-- Only a SHA-256 of the session token is stored, so a database leak cannot be replayed as a login.
CREATE TABLE sessions (
    token_hash  TEXT PRIMARY KEY,
    user_id     TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    expires_at  INTEGER NOT NULL
);
CREATE INDEX idx_sessions_user ON sessions(user_id);

CREATE TABLE scores (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id         TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    client_game_id  TEXT NOT NULL,
    mode            TEXT NOT NULL CHECK (mode IN ('Single', 'FullSheet')),
    total           INTEGER NOT NULL CHECK (total >= 0),
    played_at       INTEGER NOT NULL,
    UNIQUE (user_id, client_game_id)
);
CREATE INDEX idx_scores_board ON scores(mode, played_at, total);
CREATE INDEX idx_scores_user_time ON scores(user_id, played_at);
