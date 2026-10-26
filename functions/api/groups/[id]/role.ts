import { groupError, groupUser, nicknameBody } from "../../../_lib/groupHttp";
import { setRole } from "../../../_lib/groups";
import { json } from "../../../_lib/http";
import type { Env } from "../../../_lib/providers";

/** A group admin makes a member an admin, or an admin a plain member: { nickname, role }. The last admin cannot step down. */
export const onRequestPost: PagesFunction<Env, "id"> = async ({ request, env, params }) => {
  const who = await groupUser(request, env, true);
  if ("response" in who) {
    return who.response;
  }
  const body = await nicknameBody<{ role?: unknown }>(request);
  if (!body || (body.role !== "admin" && body.role !== "member")) {
    return json({ error: "invalid_body" }, 400);
  }
  const result = await setRole(env.DB, who.user, String(params.id), body.nickname, body.role);
  return result.ok ? json({ ok: true }) : groupError(result.error);
};