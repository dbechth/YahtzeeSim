import { getSessionUser, json } from "../_lib/http";
import type { Env } from "../_lib/providers";
import { searchPlayers } from "../_lib/challenges";

/** Nicknames matching ?q= (for inviting people to a challenge). Nicknames are already public on the leaderboard. */
export const onRequestGet: PagesFunction<Env> = async ({ request, env }) => {
  const user = await getSessionUser(request, env);
  if (!user) {
    return json({ error: "unauthorized" }, 401);
  }
  const query = new URL(request.url).searchParams.get("q") ?? "";
  return json({ players: await searchPlayers(env.DB, user.id, query) });
};
