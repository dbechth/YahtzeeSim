import { isChallengeId, markSeen } from "../../../_lib/challenges";
import { getSessionUser, isSafeMutation, json } from "../../../_lib/http";
import type { Env } from "../../../_lib/providers";

/** Clears the unread dot for this challenge. */
export const onRequestPost: PagesFunction<Env, "id"> = async ({ request, env, params }) => {
  if (!isSafeMutation(request)) {
    return json({ error: "bad_request" }, 400);
  }
  const user = await getSessionUser(request, env);
  if (!user) {
    return json({ error: "unauthorized" }, 401);
  }
  const id = String(params.id);
  if (!isChallengeId(id)) {
    return json({ error: "not_found" }, 404);
  }
  await markSeen(env.DB, user.id, id, Date.now());
  return json({ seen: true });
};
