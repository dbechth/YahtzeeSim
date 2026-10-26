import { getSessionUser, json } from "../../_lib/http";
import { searchGroups } from "../../_lib/groups";
import type { Env } from "../../_lib/providers";

/** Groups whose name matches ?q= (so players can ask to join). Group names are findable by every signed-in player. */
export const onRequestGet: PagesFunction<Env> = async ({ request, env }) => {
  const user = await getSessionUser(request, env);
  if (!user) {
    return json({ error: "unauthorized" }, 401);
  }
  const query = new URL(request.url).searchParams.get("q") ?? "";
  return json({ groups: await searchGroups(env.DB, user.id, query) });
};