import { validateStyleBadge } from "../_lib/badges";
import { getSessionUser, isSafeMutation, json } from "../_lib/http";
import type { Env } from "../_lib/providers";

/** Stores the signed-in player's play-style badge, worked out by the app from their own games. */
export const onRequestPut: PagesFunction<Env> = async ({ request, env }) => {
  if (!isSafeMutation(request)) {
    return json({ error: "bad_request" }, 400);
  }
  const user = await getSessionUser(request, env);
  if (!user) {
    return json({ error: "unauthorized" }, 401);
  }

  const update = validateStyleBadge(await request.json().catch(() => null));
  if (!update.ok) {
    return json({ error: update.error }, 400);
  }

  await env.DB.prepare("UPDATE users SET style_badge = ?1, style_badge_version = ?2 WHERE id = ?3")
    .bind(update.value.badge, update.value.version, user.id)
    .run();
  return json({ badge: update.value.badge });
};
