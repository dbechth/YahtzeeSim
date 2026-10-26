-- One row per device that allowed notifications (Web Push). The endpoint is a URL at Apple's, Google's or another
-- browser's push service; p256dh and auth are the browser's keys used to encrypt messages for it. Rows are deleted
-- when the player turns notifications off or the push service says the subscription is gone.
CREATE TABLE push_subscriptions (
    endpoint         TEXT PRIMARY KEY,
    user_id          TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    p256dh           TEXT NOT NULL,
    auth             TEXT NOT NULL,
    created_at       INTEGER NOT NULL,
    last_success_at  INTEGER
);
CREATE INDEX idx_push_subscriptions_user ON push_subscriptions(user_id);
