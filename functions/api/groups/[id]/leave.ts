import { groupError, groupUser } from "../../../_lib/groupHttp";
import { leaveGroup } from "../../../_lib/groups";
import { json } from "../../../_lib/http";
import type { Env } from "../../../_lib/providers";

/** Leaves a group or withdraws a join request. The last admin has to make someone else an admin first. */
export const onRequestPost: PagesFunction<Env, "id"> = async ({ request, env, params }) => {
  const who = await groupUser(request, env, true);
  if ("response" in who) {
    return who.response;
  }
  const result = await leaveGroup(env.DB, who.user.id, String(params.id));
  return result.ok ? json({ ok: true }) : groupError(result.error);
};