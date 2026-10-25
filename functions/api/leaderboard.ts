import { getSessionUser, json } from "../_lib/http";
import { rankRows, weekStart, type BoardRow } from "../_lib/leaderboard";
import type { Env } from "../_lib/providers";
import { parseMode, parsePeriod } from "../_lib/validate";

const TOP_N = 50;

export const onRequestGet: PagesFunction<Env> = async ({ request, env }) => {
  const user = await getSessionUser(request, env);
  if (!user) {
    return json({ error: "unauthorized" }, 401);
  }

  const params = new URL(request.url).searchParams;
  const mode = parseMode(params.get("mode") ?? "Single");
  const period = parsePeriod(params.get("period") ?? "all");
  if (!mode || !period) {
    return json({ error: "invalid_query" }, 400);
  }
  const since = period === "week" ? weekStart(Date.now()) : 0;

  const top = await env.DB.prepare(
    `SELECT u.nickname AS nickname, u.style_badge AS badge, MAX(s.total) AS best, MIN(s.total) AS low,
            CAST(ROUND(AVG(s.total)) AS INTEGER) AS avg, COUNT(*) AS games
     FROM scores s JOIN users u ON u.id = s.user_id
     WHERE s.mode = ?1 AND s.played_at >= ?2
     GROUP BY s.user_id
     ORDER BY best DESC, u.nickname
     LIMIT ?3`,
  )
    .bind(mode, since, TOP_N)
    .all<BoardRow>();

  const mine = await env.DB.prepare(
    `SELECT MAX(total) AS best, MIN(total) AS low, CAST(ROUND(AVG(total)) AS INTEGER) AS avg, COUNT(*) AS games,
            (SELECT style_badge FROM users WHERE id = ?1) AS badge
     FROM scores WHERE user_id = ?1 AND mode = ?2 AND played_at >= ?3`,
  )
    .bind(user.id, mode, since)
    .first<{ best: number | null; low: number; avg: number; games: number; badge: string | null }>();

  let me: (Omit<BoardRow, "nickname"> & { nickname: string | null; rank: number }) | null = null;
  if (mine?.best != null) {
    const ahead = await env.DB.prepare(
      `SELECT COUNT(*) AS n FROM (
         SELECT user_id FROM scores WHERE mode = ?1 AND played_at >= ?2 GROUP BY user_id HAVING MAX(total) > ?3
       )`,
    )
      .bind(mode, since, mine.best)
      .first<{ n: number }>();
    me = { nickname: user.nickname, best: mine.best, low: mine.low, avg: mine.avg, games: mine.games, badge: mine.badge ?? null, rank: (ahead?.n ?? 0) + 1 };
  }

  return json({ mode, period, entries: rankRows(top.results), me });
};
