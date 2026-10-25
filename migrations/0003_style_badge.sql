-- Play-style badge shown next to the nickname on the leaderboard. Worked out on the player's device from their own
-- recent Full Sheet games (YahtzeeSim.Analysis/Story/BadgeRules.cs) and sent with PUT /api/style; the server only
-- checks it is a known badge. It describes play style, never skill.
ALTER TABLE users ADD COLUMN style_badge TEXT;
ALTER TABLE users ADD COLUMN style_badge_version TEXT;
