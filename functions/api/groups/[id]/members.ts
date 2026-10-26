import { groupError, groupUser, nicknameBody } from "../../../_lib/groupHttp";
import { addMember } from "../../../_lib/groups";
import { json } from "../../../_lib/http";
import { notify } from "../../../_lib/notify";
import type { Env } from "../../../_lib/providers";

/** A group admin adds a player straight into the group: { nickname }. */
export const onRequestPost: PagesFunction<Env, "id"> = async ({ request, env, params, waitUntil }) => {
  const who = await groupUser(request, env, true);
  if ("response" in who) {
    return who.response;
  }
  const body = await nicknameBody(request);
  if (!body) {
    return json({ error: "invalid_body" }, 400);
  }
  const result = await addMember(env.DB, who.user, String(params.id), body.nickname, Date.now());
  if (!result.ok) {
    return groupError(result.error);
  }
  if (result.event) {
    waitUntil(notify(env, [result.event]));
  }
  return json({ ok: true, added: result.added });
};
