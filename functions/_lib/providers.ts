import { decodeJwtPayload } from "./crypto";

export interface Env {
  DB: D1Database;
  GOOGLE_CLIENT_ID?: string;
  GOOGLE_CLIENT_SECRET?: string;
  /** Web Push (VAPID) keys, see scripts/new-vapid-keys.ps1. Without them no notifications are sent. */
  VAPID_PUBLIC_KEY?: string;
  VAPID_PRIVATE_KEY?: string;
  VAPID_SUBJECT?: string;
}

export interface AuthorizeRequest {
  redirectUri: string;
  state: string;
  nonce: string;
  challenge: string;
}

export interface TokenRequest {
  code: string;
  redirectUri: string;
  verifier: string;
  nonce: string;
}

/** An identity provider only has to prove who the user is; we keep just its stable user id. */
export interface AuthProvider {
  id: string;
  isConfigured(env: Env): boolean;
  buildAuthorizeUrl(request: AuthorizeRequest, env: Env): string;
  fetchSubject(request: TokenRequest, env: Env, fetchFn?: typeof fetch): Promise<string>;
}

const GOOGLE_ISSUERS = ["https://accounts.google.com", "accounts.google.com"];

/** Checks the OIDC claims of an ID token received directly from Google's token endpoint over TLS. */
export function subjectFromGoogleIdToken(idToken: string, clientId: string, nonce: string, nowSeconds: number): string | null {
  const claims = decodeJwtPayload(idToken);
  if (!claims) {
    return null;
  }
  const { iss, aud, exp, sub, nonce: tokenNonce } = claims;
  if (typeof iss !== "string" || !GOOGLE_ISSUERS.includes(iss)) return null;
  if (aud !== clientId) return null;
  if (typeof exp !== "number" || exp <= nowSeconds) return null;
  if (tokenNonce !== nonce) return null;
  return typeof sub === "string" && sub.length > 0 ? sub : null;
}

export const google: AuthProvider = {
  id: "google",

  isConfigured: (env) => !!env.GOOGLE_CLIENT_ID && !!env.GOOGLE_CLIENT_SECRET,

  buildAuthorizeUrl(request, env) {
    const url = new URL("https://accounts.google.com/o/oauth2/v2/auth");
    url.search = new URLSearchParams({
      client_id: env.GOOGLE_CLIENT_ID!,
      redirect_uri: request.redirectUri,
      response_type: "code",
      scope: "openid",
      state: request.state,
      nonce: request.nonce,
      code_challenge: request.challenge,
      code_challenge_method: "S256",
      prompt: "select_account",
    }).toString();
    return url.toString();
  },

  async fetchSubject(request, env, fetchFn = fetch) {
    const response = await fetchFn("https://oauth2.googleapis.com/token", {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({
        code: request.code,
        client_id: env.GOOGLE_CLIENT_ID!,
        client_secret: env.GOOGLE_CLIENT_SECRET!,
        redirect_uri: request.redirectUri,
        grant_type: "authorization_code",
        code_verifier: request.verifier,
      }),
    });
    if (!response.ok) {
      throw new Error(`token_exchange_failed_${response.status}`);
    }
    const { id_token } = (await response.json()) as { id_token?: string };
    const sub = id_token
      ? subjectFromGoogleIdToken(id_token, env.GOOGLE_CLIENT_ID!, request.nonce, Math.floor(Date.now() / 1000))
      : null;
    if (!sub) {
      throw new Error("invalid_id_token");
    }
    return sub;
  },
};

export const providers: Record<string, AuthProvider> = { google };
