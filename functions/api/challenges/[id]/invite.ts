import { CHALLENGE_MAX_PLAYERS, invitePlayers, isChallengeId } from "../../../_lib/challenges";
import { getSessionUser, isSafeMutation, json } from "../../../_lib/http";
import { notify } from "../../../_lib/notify";
import type { Env } from "../../../_lib/providers";

/** Invites more players by nickname to an open challenge (any of its players may invite). */
export const onRequestPost: PagesFunction<Env, "id"> = async ({ request, env, params, waitUntil }) => {
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
  const id = String(params.id);
  const body = (await request.json().catch(() => null)) as { nicknames?: unknown } | null;
  const nicknames = body?.nicknames;
  if (
    !isChallengeId(id) ||
    !Array.isArray(nicknames) ||
    nicknames.length === 0 ||
    nicknames.length > CHALLENGE_MAX_PLAYERS ||
    nicknames.some((n) => typeof n !== "string" || n.length > 32)
  ) {
    return json({ error: "invalid_body" }, 400);
  }

  const result = await invitePlayers(env.DB, { id: user.id, nickname: user.nickname }, id, nicknames as string[], Date.now());
  if (!result.ok) {
    return json({ error: result.error }, result.error === "not_found" ? 404 : 409);
  }
  if (result.event) {
    waitUntil(notify(env, [result.event]));
  }
  return json({ added: result.added, missing: result.missing });
};
