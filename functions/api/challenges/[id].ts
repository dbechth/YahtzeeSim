import { getChallengeDetail } from "../../_lib/challenges";
import { getSessionUser, json } from "../../_lib/http";
import type { Env } from "../../_lib/providers";

/** One challenge, for its players only. Anyone else gets 404, so they cannot even tell it exists. */
export const onRequestGet: PagesFunction<Env, "id"> = async ({ request, env, params }) => {
  const user = await getSessionUser(request, env);
  if (!user) {
    return json({ error: "unauthorized" }, 401);
  }
  const detail = await getChallengeDetail(env.DB, user.id, String(params.id), Date.now());
  return detail ? json(detail) : json({ error: "not_found" }, 404);
};
