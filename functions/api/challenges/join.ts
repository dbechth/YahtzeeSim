import { joinByToken } from "../../_lib/challenges";
import { getSessionUser, isSafeMutation, json } from "../../_lib/http";
import type { Env } from "../../_lib/providers";

/** Joins a challenge with the token from an invite link. */
export const onRequestPost: PagesFunction<Env> = async ({ request, env }) => {
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

  const body = (await request.json().catch(() => null)) as { token?: unknown } | null;
  const joined = await joinByToken(env.DB, user.id, body?.token, Date.now());
  if (!joined.ok) {
    return json({ error: joined.error }, joined.error === "not_found" ? 404 : 409);
  }
  return json({ id: joined.id });
};
