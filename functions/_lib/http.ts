import { SESSION_COOKIE, SESSION_MAX_AGE_S, parseCookies } from "./cookies";
import { randomToken, sha256Hex } from "./crypto";
import type { Env } from "./providers";

export interface User {
  id: string;
  nickname: string | null;
}

export function json(body: unknown, status = 200, headers: HeadersInit = {}): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json", "Cache-Control": "no-store", ...headers },
  });
}

export function isSecure(request: Request): boolean {
  return new URL(request.url).protocol === "https:";
}

/** State-changing calls must be JSON (forces a CORS preflight cross-origin) and, when present, same-origin. */
export function isSafeMutation(request: Request): boolean {
  if (!(request.headers.get("Content-Type") ?? "").toLowerCase().startsWith("application/json")) {
    return false;
  }
  const origin = request.headers.get("Origin");
  return origin === null || origin === new URL(request.url).origin;
}

export async function getSessionUser(request: Request, env: Env): Promise<User | null> {
  const token = parseCookies(request.headers.get("Cookie"))[SESSION_COOKIE];
  if (!token) {
    return null;
  }
  const row = await env.DB.prepare(
    "SELECT u.id, u.nickname FROM sessions s JOIN users u ON u.id = s.user_id WHERE s.token_hash = ?1 AND s.expires_at > ?2",
  )
    .bind(await sha256Hex(token), Date.now())
    .first<User>();
  return row ?? null;
}

export async function deleteSession(request: Request, env: Env): Promise<void> {
  const token = parseCookies(request.headers.get("Cookie"))[SESSION_COOKIE];
  if (token) {
    await env.DB.prepare("DELETE FROM sessions WHERE token_hash = ?1").bind(await sha256Hex(token)).run();
  }
}

/** Finds or creates the account for a provider identity and starts a session; returns the raw token for the cookie. */
export async function signIn(env: Env, provider: string, subject: string): Promise<string> {
  const now = Date.now();
  await env.DB.prepare("INSERT OR IGNORE INTO users (id, provider, provider_sub, created_at) VALUES (?1, ?2, ?3, ?4)")
    .bind(crypto.randomUUID(), provider, subject, now)
    .run();
  const user = await env.DB.prepare("SELECT id FROM users WHERE provider = ?1 AND provider_sub = ?2")
    .bind(provider, subject)
    .first<{ id: string }>();

  const token = randomToken();
  await env.DB.batch([
    env.DB.prepare("DELETE FROM sessions WHERE expires_at <= ?1").bind(now),
    env.DB.prepare("INSERT INTO sessions (token_hash, user_id, expires_at) VALUES (?1, ?2, ?3)").bind(
      await sha256Hex(token),
      user!.id,
      now + SESSION_MAX_AGE_S * 1000,
    ),
  ]);
  return token;
}
