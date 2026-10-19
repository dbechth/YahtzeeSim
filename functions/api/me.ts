import { getSessionUser, isSafeMutation, json } from "../_lib/http";
import type { Env } from "../_lib/providers";
import { validateNickname } from "../_lib/validate";

export const onRequestGet: PagesFunction<Env> = async ({ request, env }) => {
  const user = await getSessionUser(request, env);
  return user ? json({ id: user.id, nickname: user.nickname }) : json({ error: "unauthorized" }, 401);
};

export const onRequestPut: PagesFunction<Env> = async ({ request, env }) => {
  if (!isSafeMutation(request)) {
    return json({ error: "bad_request" }, 400);
  }
  const user = await getSessionUser(request, env);
  if (!user) {
    return json({ error: "unauthorized" }, 401);
  }

  const body = (await request.json().catch(() => null)) as { nickname?: unknown } | null;
  const nickname = validateNickname(body?.nickname);
  if (!nickname.ok) {
    return json({ error: nickname.error }, 400);
  }

  try {
    await env.DB.prepare("UPDATE users SET nickname = ?1 WHERE id = ?2").bind(nickname.value, user.id).run();
  } catch (error) {
    if (String(error).includes("UNIQUE")) {
      return json({ error: "nickname_taken" }, 409);
    }
    throw error;
  }
  return json({ nickname: nickname.value });
};
