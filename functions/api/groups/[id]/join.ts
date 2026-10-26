import { groupError, groupUser } from "../../../_lib/groupHttp";
import { requestToJoin } from "../../../_lib/groups";
import { json } from "../../../_lib/http";
import { notify } from "../../../_lib/notify";
import type { Env } from "../../../_lib/providers";

/** Asks to join a group. A group admin has to approve it. */
export const onRequestPost: PagesFunction<Env, "id"> = async ({ request, env, params, waitUntil }) => {
  const who = await groupUser(request, env, true);
  if ("response" in who) {
    return who.response;
  }
  const result = await requestToJoin(env.DB, who.user, String(params.id), Date.now());
  if (!result.ok) {
    return groupError(result.error);
  }
  if (result.event) {
    waitUntil(notify(env, [result.event]));
  }
  return json({ status: result.status });
};