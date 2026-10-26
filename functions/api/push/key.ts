import { getSessionUser, json } from "../../_lib/http";
import type { Env } from "../../_lib/providers";

/** The VAPID public key the browser needs to subscribe. 404 while notifications are not set up on the server. */
export const onRequestGet: PagesFunction<Env> = async ({ request, env }) => {
  const user = await getSessionUser(request, env);
  if (!user) {
    return json({ error: "unauthorized" }, 401);
  }
  return env.VAPID_PUBLIC_KEY ? json({ publicKey: env.VAPID_PUBLIC_KEY }) : json({ error: "not_configured" }, 404);
};
