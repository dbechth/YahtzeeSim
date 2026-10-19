import { OAUTH_COOKIE, SESSION_COOKIE, SESSION_MAX_AGE_S, clearCookie, parseCookies, serializeCookie } from "../../_lib/cookies";
import { isSecure, signIn } from "../../_lib/http";
import { providers, type Env } from "../../_lib/providers";

interface OAuthCookie {
  provider: string;
  state: string;
  nonce: string;
  verifier: string;
}

function readOAuthCookie(request: Request): OAuthCookie | null {
  const raw = parseCookies(request.headers.get("Cookie"))[OAUTH_COOKIE];
  if (!raw) {
    return null;
  }
  try {
    const value = JSON.parse(decodeURIComponent(raw));
    return [value.provider, value.state, value.nonce, value.verifier].every((v) => typeof v === "string") ? value : null;
  } catch {
    return null;
  }
}

function redirect(location: string, cookies: string[]): Response {
  const headers = new Headers({ Location: location, "Cache-Control": "no-store" });
  for (const cookie of cookies) {
    headers.append("Set-Cookie", cookie);
  }
  return new Response(null, { status: 302, headers });
}

export const onRequestGet: PagesFunction<Env> = async ({ request, env }) => {
  const url = new URL(request.url);
  const secure = isSecure(request);
  const clearOAuth = clearCookie(OAUTH_COOKIE, "/api/auth", secure);
  const failed = redirect("/?signin=failed", [clearOAuth]);

  const saved = readOAuthCookie(request);
  const code = url.searchParams.get("code");
  const provider = saved ? providers[saved.provider] : undefined;
  if (!saved || !provider || !code || url.searchParams.get("state") !== saved.state) {
    return failed;
  }

  try {
    const subject = await provider.fetchSubject(
      { code, redirectUri: `${url.origin}/api/auth/callback`, verifier: saved.verifier, nonce: saved.nonce },
      env,
    );
    const token = await signIn(env, provider.id, subject);
    const session = serializeCookie(SESSION_COOKIE, token, { maxAge: SESSION_MAX_AGE_S, path: "/", secure });
    return redirect("/", [session, clearOAuth]);
  } catch {
    return failed;
  }
};
