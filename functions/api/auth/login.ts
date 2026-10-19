import { OAUTH_COOKIE, serializeCookie } from "../../_lib/cookies";
import { pkceChallenge, randomToken } from "../../_lib/crypto";
import { isSecure, json } from "../../_lib/http";
import { providers, type Env } from "../../_lib/providers";

export const onRequestGet: PagesFunction<Env> = async ({ request, env }) => {
  const url = new URL(request.url);
  const provider = providers[url.searchParams.get("provider") ?? "google"];
  if (!provider || !provider.isConfigured(env)) {
    return json({ error: "sign_in_not_configured" }, 503);
  }

  const state = randomToken();
  const nonce = randomToken();
  const verifier = randomToken(48);
  const location = provider.buildAuthorizeUrl(
    { redirectUri: `${url.origin}/api/auth/callback`, state, nonce, challenge: await pkceChallenge(verifier) },
    env,
  );

  const cookie = serializeCookie(OAUTH_COOKIE, encodeURIComponent(JSON.stringify({ provider: provider.id, state, nonce, verifier })), {
    maxAge: 600,
    path: "/api/auth",
    secure: isSecure(request),
  });
  return new Response(null, { status: 302, headers: { Location: location, "Set-Cookie": cookie, "Cache-Control": "no-store" } });
};
