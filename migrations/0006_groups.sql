-- Groups: players form a group, new members ask to join and a group admin approves them. Any member can invite
-- the whole group to a challenge. Group names and member counts can be found by every signed-in player (so they can ask
-- to join); the member list is only shown to the group's members and join requests only to its admins
-- (enforced in functions/_lib/groups.ts).
CREATE TABLE user_groups (
    id          TEXT PRIMARY KEY,
    name        TEXT NOT NULL,
    -- Lower-case name, so two groups cannot differ only by letter case.
    name_key    TEXT NOT NULL UNIQUE,
    created_by  TEXT REFERENCES users(id) ON DELETE SET NULL,
    created_at  INTEGER NOT NULL
);

CREATE TABLE group_members (
    group_id      TEXT NOT NULL REFERENCES user_groups(id) ON DELETE CASCADE,
    user_id       TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role          TEXT NOT NULL CHECK (role IN ('member', 'admin')),
    -- 'pending' is a join request waiting for an admin; it carries no rights until approved.
    status        TEXT NOT NULL CHECK (status IN ('pending', 'active')),
    requested_at  INTEGER NOT NULL,
    joined_at     INTEGER,
    PRIMARY KEY (group_id, user_id)
);
CREATE INDEX idx_group_members_user ON group_members(user_id, status);
