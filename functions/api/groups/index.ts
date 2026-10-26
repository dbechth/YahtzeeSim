import { createGroup, listGroups } from "../../_lib/groups";
import { groupError, groupUser } from "../../_lib/groupHttp";
import { getSessionUser, json } from "../../_lib/http";
import type { Env } from "../../_lib/providers";

/** The signed-in player's groups, and the groups they asked to join and are waiting on. */
export const onRequestGet: PagesFunction<Env> = async ({ request, env }) => {
  const user = await getSessionUser(request, env);
  if (!user) {
    return json({ error: "unauthorized" }, 401);
  }
  return json({ groups: await listGroups(env.DB, user.id) });
};

/** Creates a group. The creator is its first admin. */
export const onRequestPost: PagesFunction<Env> = async ({ request, env }) => {
  const who = await groupUser(request, env, true);
  if ("response" in who) {
    return who.response;
  }
  const body = (await request.json().catch(() => null)) as { name?: unknown } | null;
  const created = await createGroup(env.DB, who.user, body?.name, Date.now());
  if (!created.ok) {
    return created.error === "invalid_name" ? json({ error: created.error }, 400) : groupError(created.error);
  }
  return json({ id: created.id }, 201);
};