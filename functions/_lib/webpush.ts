import { base64Url } from "./crypto";

/**
 * Standard Web Push (RFC 8030) from a Cloudflare Worker, using only WebCrypto:
 * the message is encrypted for the browser with aes128gcm (RFC 8291 / RFC 8188) and the request is signed with a
 * VAPID JWT (RFC 8292, ES256). Works with Apple (iOS/iPadOS/Safari), Google (Chrome/Android), Mozilla and Microsoft.
 */

export interface PushSubscriptionKeys {
  endpoint: string;
  /** Browser's P-256 public key, base64url (65 bytes uncompressed). */
  p256dh: string;
  /** Browser's 16-byte auth secret, base64url. */
  auth: string;
}

export interface VapidKeys {
  /** P-256 public key, base64url of the 65-byte uncompressed point (shared with the browser). */
  publicKey: string;
  /** P-256 private key, base64url of the 32-byte scalar. */
  privateKey: string;
  /** Contact for push services, e.g. "mailto:owner@example.com". */
  subject: string;
}

export type PushOutcome = "sent" | "gone" | "failed";

export function base64UrlDecode(text: string): Uint8Array {
  const padded = text.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((text.length + 3) % 4);
  return Uint8Array.from(atob(padded), (c) => c.charCodeAt(0));
}

const encoder = new TextEncoder();

function concat(...parts: Uint8Array[]): Uint8Array {
  const out = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
  let offset = 0;
  for (const p of parts) {
    out.set(p, offset);
    offset += p.length;
  }
  return out;
}

async function hmac(key: Uint8Array, data: Uint8Array): Promise<Uint8Array> {
  const k = await crypto.subtle.importKey("raw", key, { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  return new Uint8Array(await crypto.subtle.sign("HMAC", k, data));
}

/** P-256 JWK from a raw public point (and optionally the private scalar). */
function ecJwk(publicPoint: Uint8Array, privateScalar?: Uint8Array): JsonWebKey {
  if (publicPoint.length !== 65 || publicPoint[0] !== 4) {
    throw new Error("Expected an uncompressed P-256 public key");
  }
  return {
    kty: "EC",
    crv: "P-256",
    x: base64Url(publicPoint.slice(1, 33)),
    y: base64Url(publicPoint.slice(33, 65)),
    ...(privateScalar ? { d: base64Url(privateScalar) } : {}),
    ext: true,
  };
}

/**
 * Encrypts a payload for one subscription (RFC 8291). <paramref name="test"/> fixes the server key pair and salt so
 * the RFC's worked example can be reproduced; normally both are random.
 */
export async function encryptPayload(
  subscription: Pick<PushSubscriptionKeys, "p256dh" | "auth">,
  payload: Uint8Array,
  test?: { serverPublic: Uint8Array; serverPrivate: Uint8Array; salt: Uint8Array },
): Promise<Uint8Array> {
  const uaPublic = base64UrlDecode(subscription.p256dh);
  const authSecret = base64UrlDecode(subscription.auth);

  let asPublic: Uint8Array;
  let asPrivateKey: CryptoKey;
  if (test) {
    asPublic = test.serverPublic;
    asPrivateKey = await crypto.subtle.importKey("jwk", ecJwk(test.serverPublic, test.serverPrivate), { name: "ECDH", namedCurve: "P-256" }, false, ["deriveBits"]);
  } else {
    const pair = (await crypto.subtle.generateKey({ name: "ECDH", namedCurve: "P-256" }, true, ["deriveBits"])) as CryptoKeyPair;
    asPublic = new Uint8Array((await crypto.subtle.exportKey("raw", pair.publicKey)) as ArrayBuffer);
    asPrivateKey = pair.privateKey;
  }

  const uaKey = await crypto.subtle.importKey("jwk", ecJwk(uaPublic), { name: "ECDH", namedCurve: "P-256" }, false, []);
  // Cloudflare's typings call this field `$public`; the runtime (and Node) read `public`.
  const ecdhParams = { name: "ECDH", public: uaKey } as unknown as SubtleCryptoDeriveKeyAlgorithm;
  const ecdhSecret = new Uint8Array(await crypto.subtle.deriveBits(ecdhParams, asPrivateKey, 256));

  // IKM = HKDF(salt = auth secret, ikm = ECDH secret, info = "WebPush: info" || 0 || ua_public || as_public, 32 bytes)
  const prkKey = await hmac(authSecret, ecdhSecret);
  const keyInfo = concat(encoder.encode("WebPush: info\0"), uaPublic, asPublic, new Uint8Array([1]));
  const ikm = await hmac(prkKey, keyInfo);

  const salt = test?.salt ?? crypto.getRandomValues(new Uint8Array(16));
  const prk = await hmac(salt, ikm);
  const cek = (await hmac(prk, concat(encoder.encode("Content-Encoding: aes128gcm\0"), new Uint8Array([1])))).slice(0, 16);
  const nonce = (await hmac(prk, concat(encoder.encode("Content-Encoding: nonce\0"), new Uint8Array([1])))).slice(0, 12);

  // One record: the payload followed by the 0x02 "last record" delimiter.
  const aesKey = await crypto.subtle.importKey("raw", cek, "AES-GCM", false, ["encrypt"]);
  const ciphertext = new Uint8Array(await crypto.subtle.encrypt({ name: "AES-GCM", iv: nonce }, aesKey, concat(payload, new Uint8Array([2]))));

  const recordSize = new Uint8Array([0, 0, 0x10, 0]); // 4096
  return concat(salt, recordSize, new Uint8Array([asPublic.length]), asPublic, ciphertext);
}

/** VAPID Authorization header value for an endpoint (ES256 JWT valid for 12 hours). */
export async function vapidAuthorization(endpoint: string, keys: VapidKeys, nowSeconds = Math.floor(Date.now() / 1000)): Promise<string> {
  const header = base64Url(encoder.encode(JSON.stringify({ typ: "JWT", alg: "ES256" })));
  const claims = base64Url(
    encoder.encode(JSON.stringify({ aud: new URL(endpoint).origin, exp: nowSeconds + 12 * 60 * 60, sub: keys.subject })),
  );
  const signingKey = await crypto.subtle.importKey(
    "jwk",
    ecJwk(base64UrlDecode(keys.publicKey), base64UrlDecode(keys.privateKey)),
    { name: "ECDSA", namedCurve: "P-256" },
    false,
    ["sign"],
  );
  // WebCrypto returns r || s (64 bytes), which is exactly the JWS ES256 signature format.
  const signature = new Uint8Array(await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, signingKey, encoder.encode(`${header}.${claims}`)));
  return `vapid t=${header}.${claims}.${base64Url(signature)}, k=${keys.publicKey}`;
}

/** Sends one push message. "gone" means the subscription no longer exists and should be deleted. */
export async function sendPush(
  subscription: PushSubscriptionKeys,
  message: unknown,
  keys: VapidKeys,
  options: { ttlSeconds?: number; urgency?: "normal" | "high"; topic?: string } = {},
  fetchFn: typeof fetch = fetch,
): Promise<PushOutcome> {
  const body = await encryptPayload(subscription, encoder.encode(JSON.stringify(message)));
  const headers: Record<string, string> = {
    Authorization: await vapidAuthorization(subscription.endpoint, keys),
    "Content-Encoding": "aes128gcm",
    "Content-Type": "application/octet-stream",
    TTL: String(options.ttlSeconds ?? 24 * 60 * 60),
    Urgency: options.urgency ?? "normal",
  };
  if (options.topic) {
    headers.Topic = options.topic;
  }

  try {
    const response = await fetchFn(subscription.endpoint, { method: "POST", headers, body });
    if (response.status === 404 || response.status === 410) {
      return "gone";
    }
    return response.ok ? "sent" : "failed";
  } catch {
    return "failed";
  }
}

/** Push endpoints must be https URLs; everything else is refused when a browser registers. */
export function isValidSubscription(value: unknown): value is PushSubscriptionKeys {
  if (typeof value !== "object" || value === null) {
    return false;
  }
  const { endpoint, p256dh, auth } = value as Record<string, unknown>;
  if (typeof endpoint !== "string" || endpoint.length > 1000 || typeof p256dh !== "string" || typeof auth !== "string") {
    return false;
  }
  try {
    const url = new URL(endpoint);
    return url.protocol === "https:" && base64UrlDecode(p256dh).length === 65 && base64UrlDecode(auth).length === 16;
  } catch {
    return false;
  }
}
