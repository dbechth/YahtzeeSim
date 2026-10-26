import { getGroupDetail } from "../../_lib/groups";
import { getSessionUser, json } from "../../_lib/http";
import type { Env } from "../../_lib/providers";

/** One group with its members, for its members only. Anyone else gets 404. Admins also get the join requests. */
export const onRequestGet: PagesFunction<Env, "id"> = async ({ request, env, params }) => {
  const user = await getSessionUser(request, env);
  if (!user) {
    return json({ error: "unauthorized" }, 401);
  }
  const detail = await getGroupDetail(env.DB, user.id, String(params.id));
  return detail ? json(detail) : json({ error: "not_found" }, 404);
};