import { recordEntry } from "../_lib/challenges";
import { getSessionUser, isSafeMutation, json } from "../_lib/http";
import { notify } from "../_lib/notify";
import type { Env } from "../_lib/providers";
import { MAX_SCORES_PER_HOUR, validateScoreSubmission } from "../_lib/validate";

export const onRequestPost: PagesFunction<Env> = async ({ request, env, waitUntil }) => {
  if (!isSafeMutation(request)) {
    return json({ error: "bad_request" }, 400);
  }
  const user = await getSessionUser(request, env);
  if (!user) {
    return json({ error: "unauthorized" }, 401);
  }
  if (!user.nickname) {
    return json({ error: "nickname_required" }, 409);
  }

  const submission = validateScoreSubmission(await request.json().catch(() => null));
  if (!submission.ok) {
    return json({ error: submission.error }, 400);
  }
  const { gameId, mode, total } = submission.value;

  const now = Date.now();
  const recent = await env.DB.prepare("SELECT COUNT(*) AS n FROM scores WHERE user_id = ?1 AND played_at > ?2")
    .bind(user.id, now - 3_600_000)
    .first<{ n: number }>();
  if ((recent?.n ?? 0) >= MAX_SCORES_PER_HOUR) {
    return json({ error: "rate_limited" }, 429);
  }

  // Re-sending a game id replaces its score, so retries are safe and an undone final move can be re-scored.
  await env.DB.prepare(
    `INSERT INTO scores (user_id, client_game_id, mode, total, played_at) VALUES (?1, ?2, ?3, ?4, ?5)
     ON CONFLICT (user_id, client_game_id) DO UPDATE SET total = excluded.total, played_at = excluded.played_at`,
  )
    .bind(user.id, gameId, mode, total, now)
    .run();

  // A challenge game is a normal score that is also linked to the challenge (once, while it is open).
  if (submission.value.challengeId) {
    const entry = await recordEntry(env.DB, { id: user.id, nickname: user.nickname }, submission.value.challengeId, { gameId, mode, total }, now);
    if (entry.events.length > 0) {
      waitUntil(notify(env, entry.events));
    }
    return json({ accepted: true, challenge: { recorded: entry.recorded, reason: entry.reason ?? null } });
  }

  return json({ accepted: true });
};
