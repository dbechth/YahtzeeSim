import { getSessionUser, isSafeMutation, json } from "../_lib/http";
import type { Env } from "../_lib/providers";
import { MAX_ACTIONS_PER_GAME, MAX_ACTIONS_PER_HOUR, validateActionBatch } from "../_lib/validate";

// The validated batch is passed as one JSON parameter and expanded by SQLite, so a batch is a single D1 query.
const INSERT_ACTIONS = `
  INSERT OR IGNORE INTO game_actions
    (user_id, client_game_id, seq, mode, action, occurred_at, received_at, turn, roll_number, dice, die_id, game_index, category, points, yahtzee_bonus)
  SELECT ?1, ?2, json_extract(j.value, '$.seq'), ?3, json_extract(j.value, '$.type'), json_extract(j.value, '$.at'), ?4,
         json_extract(j.value, '$.turn'), json_extract(j.value, '$.roll'), json_extract(j.value, '$.dice'),
         json_extract(j.value, '$.dieId'), json_extract(j.value, '$.gameIndex'),
         json_extract(j.value, '$.category'), json_extract(j.value, '$.points'), json_extract(j.value, '$.bonus')
  FROM json_each(?5) AS j`;

export const onRequestPost: PagesFunction<Env> = async ({ request, env }) => {
  if (!isSafeMutation(request)) {
    return json({ error: "bad_request" }, 400);
  }
  const user = await getSessionUser(request, env);
  if (!user) {
    return json({ error: "unauthorized" }, 401);
  }

  const now = Date.now();
  const batch = validateActionBatch(await request.json().catch(() => null), now);
  if (!batch.ok) {
    return json({ error: batch.error }, 400);
  }
  const { gameId, mode, actions } = batch.value;

  const recent = await env.DB.prepare("SELECT COUNT(*) AS n FROM game_actions WHERE user_id = ?1 AND received_at > ?2")
    .bind(user.id, now - 3_600_000)
    .first<{ n: number }>();
  if ((recent?.n ?? 0) + actions.length > MAX_ACTIONS_PER_HOUR) {
    return json({ error: "rate_limited" }, 429);
  }

  const inGame = await env.DB.prepare("SELECT COUNT(*) AS n FROM game_actions WHERE user_id = ?1 AND client_game_id = ?2")
    .bind(user.id, gameId)
    .first<{ n: number }>();
  if ((inGame?.n ?? 0) + actions.length > MAX_ACTIONS_PER_GAME) {
    return json({ error: "game_log_full" }, 413);
  }

  // Re-sent actions (same game and sequence number) are ignored, so retries are safe.
  await env.DB.prepare(INSERT_ACTIONS).bind(user.id, gameId, mode, now, JSON.stringify(actions)).run();

  return json({ accepted: actions.length });
};
