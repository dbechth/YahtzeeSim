import { getSessionUser, json } from "../_lib/http";
import type { Env } from "../_lib/providers";
import { buildStrategyPayload, STRATEGY_GAME_LIMIT, type StrategyActionRow, type StrategyGameRow } from "../_lib/strategy";

/** The signed-in player's last finished Full Sheet games and their actions, for the in-app strategy report. */
export const onRequestGet: PagesFunction<Env> = async ({ request, env }) => {
  const user = await getSessionUser(request, env);
  if (!user) {
    return json({ error: "unauthorized" }, 401);
  }

  const games = await env.DB.prepare(
    `SELECT client_game_id, total, played_at FROM scores
     WHERE user_id = ?1 AND mode = 'FullSheet'
     ORDER BY played_at DESC, id DESC
     LIMIT ?2`,
  )
    .bind(user.id, STRATEGY_GAME_LIMIT)
    .all<StrategyGameRow>();

  const ids = games.results.map((g) => g.client_game_id);
  if (ids.length === 0) {
    return json(buildStrategyPayload(user.nickname, [], []));
  }

  const placeholders = ids.map((_, i) => `?${i + 2}`).join(", ");
  const actions = await env.DB.prepare(
    `SELECT client_game_id, seq, action, occurred_at, turn, roll_number, dice, die_id, game_index, category, points
     FROM game_actions
     WHERE user_id = ?1 AND client_game_id IN (${placeholders})
     ORDER BY client_game_id, seq`,
  )
    .bind(user.id, ...ids)
    .all<StrategyActionRow>();

  return json(buildStrategyPayload(user.nickname, games.results, actions.results));
};
