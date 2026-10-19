import { describe, expect, it } from "vitest";
import { clearCookie, parseCookies, serializeCookie } from "../functions/_lib/cookies";
import { base64Url, decodeJwtPayload, pkceChallenge, sha256Hex } from "../functions/_lib/crypto";
import { rankRows, weekStart } from "../functions/_lib/leaderboard";
import { google, subjectFromGoogleIdToken } from "../functions/_lib/providers";

function jwt(payload: object): string {
  const part = (value: object) => base64Url(new TextEncoder().encode(JSON.stringify(value)));
  return `${part({ alg: "RS256" })}.${part(payload)}.signature`;
}

describe("cookies", () => {
  it("parses a Cookie header", () => {
    expect(parseCookies("a=1; yz_session=abc=; b=2")).toEqual({ a: "1", yz_session: "abc=", b: "2" });
    expect(parseCookies(null)).toEqual({});
  });

  it("serializes session cookies as HttpOnly, SameSite=Lax and Secure on https", () => {
    const cookie = serializeCookie("yz_session", "tok", { maxAge: 60, path: "/", secure: true });
    expect(cookie).toBe("yz_session=tok; Path=/; Max-Age=60; HttpOnly; SameSite=Lax; Secure");
    expect(serializeCookie("x", "y", { maxAge: 1, path: "/", secure: false })).not.toContain("Secure");
  });

  it("clears a cookie with Max-Age=0", () => {
    expect(clearCookie("x", "/api", false)).toContain("Max-Age=0");
  });
});

describe("crypto helpers", () => {
  it("matches the RFC 7636 PKCE example", async () => {
    expect(await pkceChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")).toBe("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
  });

  it("hashes with SHA-256", async () => {
    expect(await sha256Hex("abc")).toBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
  });

  it("decodes JWT payloads and rejects junk", () => {
    expect(decodeJwtPayload(jwt({ sub: "1" }))).toEqual({ sub: "1" });
    expect(decodeJwtPayload("nope")).toBeNull();
    expect(decodeJwtPayload("a.!!!.c")).toBeNull();
  });
});

describe("Google ID token claims", () => {
  const now = 1_000_000;
  const good = { iss: "https://accounts.google.com", aud: "client", exp: now + 60, nonce: "n", sub: "12345" };

  it("returns the subject for a valid token", () => {
    expect(subjectFromGoogleIdToken(jwt(good), "client", "n", now)).toBe("12345");
  });

  it.each([
    ["wrong issuer", { iss: "https://evil.example" }],
    ["wrong audience", { aud: "other" }],
    ["expired", { exp: now - 1 }],
    ["wrong nonce", { nonce: "x" }],
    ["missing subject", { sub: undefined }],
  ])("rejects a token with %s", (_name, override) => {
    expect(subjectFromGoogleIdToken(jwt({ ...good, ...override }), "client", "n", now)).toBeNull();
  });

  it("builds a PKCE authorization URL that only asks for openid", () => {
    const url = new URL(
      google.buildAuthorizeUrl(
        { redirectUri: "https://x.test/api/auth/callback", state: "s", nonce: "n", challenge: "c" },
        { GOOGLE_CLIENT_ID: "client", GOOGLE_CLIENT_SECRET: "secret" } as never,
      ),
    );
    expect(url.origin + url.pathname).toBe("https://accounts.google.com/o/oauth2/v2/auth");
    expect(url.searchParams.get("scope")).toBe("openid");
    expect(url.searchParams.get("code_challenge_method")).toBe("S256");
    expect(url.searchParams.get("state")).toBe("s");
  });

  it("exchanges the code and returns the subject", async () => {
    const fetchFn = (async (_url: string, init: RequestInit) => {
      expect(String(init.body)).toContain("code_verifier=v");
      return new Response(JSON.stringify({ id_token: jwt({ ...good, exp: Math.floor(Date.now() / 1000) + 60 }) }));
    }) as unknown as typeof fetch;
    const subject = await google.fetchSubject(
      { code: "c", redirectUri: "r", verifier: "v", nonce: "n" },
      { GOOGLE_CLIENT_ID: "client", GOOGLE_CLIENT_SECRET: "secret" } as never,
      fetchFn,
    );
    expect(subject).toBe("12345");
  });

  it("fails when Google rejects the code", async () => {
    const fetchFn = (async () => new Response("{}", { status: 400 })) as unknown as typeof fetch;
    await expect(
      google.fetchSubject({ code: "c", redirectUri: "r", verifier: "v", nonce: "n" }, { GOOGLE_CLIENT_ID: "a", GOOGLE_CLIENT_SECRET: "b" } as never, fetchFn),
    ).rejects.toThrow();
  });
});

describe("leaderboard helpers", () => {
  it("shares ranks between equal scores and skips the next rank", () => {
    const ranked = rankRows([
      { nickname: "a", best: 300 },
      { nickname: "b", best: 250 },
      { nickname: "c", best: 250 },
      { nickname: "d", best: 100 },
    ]);
    expect(ranked.map((r) => r.rank)).toEqual([1, 2, 2, 4]);
    expect(rankRows([])).toEqual([]);
  });

  it("starts the week on Monday 00:00 UTC", () => {
    // Wednesday 2026-10-21 15:30 UTC -> Monday 2026-10-19 00:00 UTC
    expect(weekStart(Date.UTC(2026, 9, 21, 15, 30))).toBe(Date.UTC(2026, 9, 19));
    // Sunday 2026-10-25 23:59 UTC is still the same week
    expect(weekStart(Date.UTC(2026, 9, 25, 23, 59))).toBe(Date.UTC(2026, 9, 19));
    // Monday itself
    expect(weekStart(Date.UTC(2026, 9, 19, 0, 0))).toBe(Date.UTC(2026, 9, 19));
  });
});
