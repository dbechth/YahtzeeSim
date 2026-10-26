import { groupError, groupUser, nicknameBody } from "../../../_lib/groupHttp";
import { removeMember } from "../../../_lib/groups";
import { json } from "../../../_lib/http";
import type { Env } from "../../../_lib/providers";

/** A group admin removes a member: { nickname }. */
export const onRequestPost: PagesFunction<Env, "id"> = async ({ request, env, params }) => {
  const who = await groupUser(request, env, true);
  if ("response" in who) {
    return who.response;
  }
  const body = await nicknameBody(request);
  if (!body) {
    return json({ error: "invalid_body" }, 400);
  }
  const result = await removeMember(env.DB, who.user, String(params.id), body.nickname);
  return result.ok ? json({ ok: true }) : groupError(result.error);
};