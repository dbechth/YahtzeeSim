import { createChallenge, listChallenges, validateNewChallenge } from "../../_lib/challenges";
import { getSessionUser, isSafeMutation, json } from "../../_lib/http";
import { notify } from "../../_lib/notify";
import type { Env } from "../../_lib/providers";

/** The signed-in player's challenges (open first, then recent finished ones). */
export const onRequestGet: PagesFunction<Env> = async ({ request, env }) => {
  const user = await getSessionUser(request, env);
  if (!user) {
    return json({ error: "unauthorized" }, 401);
  }
  return json({ challenges: await listChallenges(env.DB, user.id, Date.now()) });
};

/** Creates a challenge and invites players by nickname. Returns the invite link token for sharing. */
export const onRequestPost: PagesFunction<Env> = async ({ request, env, waitUntil }) => {
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

  const now = Date.now();
  const input = validateNewChallenge(await request.json().catch(() => null), now);
  if (!input.ok) {
    return json({ error: input.error }, 400);
  }

  const created = await createChallenge(env.DB, { id: user.id, nickname: user.nickname }, input.value, now);
  if (!created.ok) {
    return json({ error: created.error }, created.error === "rate_limited" ? 429 : 400);
  }
  if (created.event) {
    waitUntil(notify(env, [created.event]));
  }
  return json({ id: created.id, inviteToken: created.inviteToken, missing: created.missing }, 201);
};
