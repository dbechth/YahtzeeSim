import { SESSION_COOKIE, clearCookie } from "../../_lib/cookies";
import { deleteSession, isSafeMutation, isSecure, json } from "../../_lib/http";
import type { Env } from "../../_lib/providers";

export const onRequestPost: PagesFunction<Env> = async ({ request, env }) => {
  if (!isSafeMutation(request)) {
    return json({ error: "bad_request" }, 400);
  }
  await deleteSession(request, env);
  return json({ ok: true }, 200, { "Set-Cookie": clearCookie(SESSION_COOKIE, "/", isSecure(request)) });
};
