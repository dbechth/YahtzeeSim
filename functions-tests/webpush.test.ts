import { describe, expect, it } from "vitest";
import { base64Url } from "../functions/_lib/crypto";
import { base64UrlDecode, encryptPayload, isValidSubscription, sendPush, vapidAuthorization } from "../functions/_lib/webpush";

// RFC 8291 section 5 worked example.
const rfc = {
  plaintext: "When I grow up, I want to be a watermelon",
  uaPublic: "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4",
  authSecret: "BTBZMqHH6r4Tts7J_aSIgg",
  asPublic: "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8",
  asPrivate: "yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw",
  salt: "DGv6ra1nlYgDCS1FRnbzlw",
  body:
    "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN",
};

async function newKeys() {
  const pair = (await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"])) as CryptoKeyPair;
  const raw = new Uint8Array((await crypto.subtle.exportKey("raw", pair.publicKey)) as ArrayBuffer);
  const jwk = (await crypto.subtle.exportKey("jwk", pair.privateKey)) as JsonWebKey;
  return { pair, publicKey: base64Url(raw), privateKey: jwk.d! };
}

describe("web push encryption", () => {
  it("reproduces the RFC 8291 example exactly", async () => {
    const body = await encryptPayload({ p256dh: rfc.uaPublic, auth: rfc.authSecret }, new TextEncoder().encode(rfc.plaintext), {
      serverPublic: base64UrlDecode(rfc.asPublic),
      serverPrivate: base64UrlDecode(rfc.asPrivate),
      salt: base64UrlDecode(rfc.salt),
    });
    expect(base64Url(body)).toBe(rfc.body);
  });

  it("uses a fresh key and salt for every message", async () => {
    const sub = { p256dh: rfc.uaPublic, auth: rfc.authSecret };
    const a = await encryptPayload(sub, new TextEncoder().encode("hi"));
    const b = await encryptPayload(sub, new TextEncoder().encode("hi"));
    expect(base64Url(a)).not.toBe(base64Url(b));
    expect(a[20]).toBe(65);
  });
});

describe("VAPID", () => {
  it("signs a JWT for the push service origin that verifies with the public key", async () => {
    const keys = await newKeys();
    const header = await vapidAuthorization("https://web.push.apple.com/abc", { ...keys, subject: "mailto:me@x.test" }, 1_000_000);
    const [, jwt, k] = header.match(/^vapid t=([^,]+), k=(.+)$/)!;
    expect(k).toBe(keys.publicKey);
    const [h, c, s] = jwt.split(".");
    const claims = JSON.parse(new TextDecoder().decode(base64UrlDecode(c)));
    expect(claims).toEqual({ aud: "https://web.push.apple.com", exp: 1_000_000 + 43_200, sub: "mailto:me@x.test" });
    const ok = await crypto.subtle.verify(
      { name: "ECDSA", hash: "SHA-256" },
      keys.pair.publicKey,
      base64UrlDecode(s),
      new TextEncoder().encode(`${h}.${c}`),
    );
    expect(ok).toBe(true);
  });

  it("posts an encrypted message and reports gone subscriptions", async () => {
    const keys = { ...(await newKeys()), subject: "mailto:me@x.test" };
    const sub = { endpoint: "https://fcm.googleapis.com/fcm/send/xyz", p256dh: rfc.uaPublic, auth: rfc.authSecret };
    let seen: RequestInit | undefined;
    const ok = await sendPush(sub, { title: "t" }, keys, { urgency: "high", topic: "challenge-1" }, (async (_u: string, init: RequestInit) => {
      seen = init;
      return new Response(null, { status: 201 });
    }) as unknown as typeof fetch);
    expect(ok).toBe("sent");
    const headers = seen!.headers as Record<string, string>;
    expect(headers["Content-Encoding"]).toBe("aes128gcm");
    expect(headers.Urgency).toBe("high");
    expect(headers.Topic).toBe("challenge-1");
    expect(headers.Authorization).toMatch(/^vapid t=/);

    const gone = await sendPush(sub, {}, keys, {}, (async () => new Response(null, { status: 410 })) as unknown as typeof fetch);
    expect(gone).toBe("gone");
    const failed = await sendPush(sub, {}, keys, {}, (async () => { throw new Error("offline"); }) as unknown as typeof fetch);
    expect(failed).toBe("failed");
  });

  it("accepts only https endpoints with well-formed keys", () => {
    const good = { endpoint: "https://fcm.googleapis.com/x", p256dh: rfc.uaPublic, auth: rfc.authSecret };
    expect(isValidSubscription(good)).toBe(true);
    expect(isValidSubscription({ ...good, endpoint: "http://fcm.googleapis.com/x" })).toBe(false);
    expect(isValidSubscription({ ...good, p256dh: "abc" })).toBe(false);
    expect(isValidSubscription({ ...good, auth: rfc.uaPublic })).toBe(false);
    expect(isValidSubscription(null)).toBe(false);
  });
});
