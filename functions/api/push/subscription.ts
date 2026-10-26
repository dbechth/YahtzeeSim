import { getSessionUser, isSafeMutation, json } from "../../_lib/http";
import type { Env } from "../../_lib/providers";
import { isValidSubscription } from "../../_lib/webpush";

/** Most devices one player keeps notifications on; the oldest are dropped beyond this. */
export const MAX_DEVICES_PER_USER = 10;

/** Saves this device's push subscription for the signed-in player (turns notifications on). */
export const onRequestPut: PagesFunction<Env> = async ({ request, env }) => {
  if (!isSafeMutation(request)) {
    return json({ error: "bad_request" }, 400);
  }
  const user = await getSessionUser(request, env);
  if (!user) {
    return json({ error: "unauthorized" }, 401);
  }
  const body = await request.json().catch(() => null);
  if (!isValidSubscription(body)) {
    return json({ error: "invalid_subscription" }, 400);
  }

  const now = Date.now();
  await env.DB.batch([
    env.DB.prepare(
      `INSERT INTO push_subscriptions (endpoint, user_id, p256dh, auth, created_at) VALUES (?1, ?2, ?3, ?4, ?5)
       ON CONFLICT (endpoint) DO UPDATE SET user_id = excluded.user_id, p256dh = excluded.p256dh, auth = excluded.auth`,
    ).bind(body.endpoint, user.id, body.p256dh, body.auth, now),
    env.DB.prepare(
      `DELETE FROM push_subscriptions WHERE user_id = ?1 AND endpoint NOT IN (
         SELECT endpoint FROM push_subscriptions WHERE user_id = ?1 ORDER BY created_at DESC LIMIT ?2)`,
    ).bind(user.id, MAX_DEVICES_PER_USER),
  ]);
  return json({ subscribed: true });
};

/** Removes this device's subscription (turns notifications off). */
export const onRequestDelete: PagesFunction<Env> = async ({ request, env }) => {
  if (!isSafeMutation(request)) {
    return json({ error: "bad_request" }, 400);
  }
  const user = await getSessionUser(request, env);
  if (!user) {
    return json({ error: "unauthorized" }, 401);
  }
  const body = (await request.json().catch(() => null)) as { endpoint?: unknown } | null;
  if (typeof body?.endpoint !== "string") {
    return json({ error: "invalid_body" }, 400);
  }
  await env.DB.prepare("DELETE FROM push_subscriptions WHERE endpoint = ?1 AND user_id = ?2").bind(body.endpoint, user.id).run();
  return json({ subscribed: false });
};
