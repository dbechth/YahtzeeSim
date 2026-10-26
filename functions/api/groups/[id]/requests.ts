import { groupError, groupUser, nicknameBody } from "../../../_lib/groupHttp";
import { decideRequest } from "../../../_lib/groups";
import { json } from "../../../_lib/http";
import { notify } from "../../../_lib/notify";
import type { Env } from "../../../_lib/providers";

/** A group admin approves or declines a join request: { nickname, approve }. */
export const onRequestPost: PagesFunction<Env, "id"> = async ({ request, env, params, waitUntil }) => {
  const who = await groupUser(request, env, true);
  if ("response" in who) {
    return who.response;
  }
  const body = await nicknameBody<{ approve?: unknown }>(request);
  if (!body || typeof body.approve !== "boolean") {
    return json({ error: "invalid_body" }, 400);
  }
  const result = await decideRequest(env.DB, who.user, String(params.id), body.nickname, body.approve, Date.now());
  if (!result.ok) {
    return groupError(result.error);
  }
  if (result.event) {
    waitUntil(notify(env, [result.event]));
  }
  return json({ ok: true });
};