import { beforeEach, describe, expect, it } from "vitest";
import { base64Url } from "../functions/_lib/crypto";
import { notify } from "../functions/_lib/notify";
import { onRequestGet as getKey } from "../functions/api/push/key";
import { onRequestDelete as unsubscribe, onRequestPut as subscribe } from "../functions/api/push/subscription";
import { addUser, call, createTestDb, request, testEnv, type TestUser } from "./d1";

const p256dh = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
const auth = "BTBZMqHH6r4Tts7J_aSIgg";

let db: D1Database;
let gaye: TestUser;
let doug: TestUser;
let vapid: { VAPID_PUBLIC_KEY: string; VAPID_PRIVATE_KEY: string; VAPID_SUBJECT: string };

beforeEach(async () => {
  db = createTestDb();
  gaye = await addUser(db, "Grandma Gaye");
  doug = await addUser(db, "Doug");
  const pair = (await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign"])) as CryptoKeyPair;
  vapid = {
    VAPID_PUBLIC_KEY: base64Url(new Uint8Array((await crypto.subtle.exportKey("raw", pair.publicKey)) as ArrayBuffer)),
    VAPID_PRIVATE_KEY: ((await crypto.subtle.exportKey("jwk", pair.privateKey)) as JsonWebKey).d!,
    VAPID_SUBJECT: "mailto:owner@x.test",
  };
});

const subscription = (n: number) => ({ endpoint: `https://push.example.test/device-${n}`, p256dh, auth });

describe("push subscriptions", () => {
  it("serves the public key only when configured", async () => {
    expect((await call(getKey, testEnv(db), request(gaye, "GET", "/"))).status).toBe(404);
    const configured = await call(getKey, testEnv(db, vapid), request(gaye, "GET", "/"));
    expect(configured.body).toEqual({ publicKey: vapid.VAPID_PUBLIC_KEY });
  });

  it("saves, moves between accounts and removes a device", async () => {
    expect((await call(subscribe, testEnv(db), request(gaye, "PUT", "/", subscription(1)))).status).toBe(200);
    // The same device signing in as someone else now belongs to them.
    await call(subscribe, testEnv(db), request(doug, "PUT", "/", subscription(1)));
    const owner = await db.prepare("SELECT user_id FROM push_subscriptions WHERE endpoint = ?1").bind(subscription(1).endpoint).first<{ user_id: string }>();
    expect(owner?.user_id).toBe(doug.id);

    // Only the owner can remove it.
    await call(unsubscribe, testEnv(db), request(gaye, "DELETE", "/", { endpoint: subscription(1).endpoint }));
    expect(await count()).toBe(1);
    await call(unsubscribe, testEnv(db), request(doug, "DELETE", "/", { endpoint: subscription(1).endpoint }));
    expect(await count()).toBe(0);
  });

  it("rejects bad subscriptions and keeps at most ten devices per player", async () => {
    expect((await call(subscribe, testEnv(db), request(gaye, "PUT", "/", { endpoint: "http://insecure.test", p256dh, auth }))).status).toBe(400);
    for (let i = 0; i < 12; i++) {
      await call(subscribe, testEnv(db), request(gaye, "PUT", "/", subscription(i)));
    }
    expect(await count()).toBe(10);
  });
});

describe("notify", () => {
  it("sends to every device of the event's players only, and drops gone subscriptions", async () => {
    await call(subscribe, testEnv(db), request(gaye, "PUT", "/", subscription(1)));
    await call(subscribe, testEnv(db), request(doug, "PUT", "/", subscription(2)));
    await call(subscribe, testEnv(db), request(doug, "PUT", "/", subscription(3)));
    const hits: string[] = [];
    const fetchFn = (async (url: string) => {
      hits.push(url);
      return new Response(null, { status: url.endsWith("device-3") ? 410 : 201 });
    }) as unknown as typeof fetch;

    const sent = await notify(
      testEnv(db, vapid),
      [{ kind: "entry", challengeId: "c".repeat(22), title: "Showdown", nickname: "Grandma Gaye", total: 1725, userIds: [doug.id] }],
      fetchFn,
    );
    expect(sent).toBe(1);
    expect(hits.sort()).toEqual([subscription(2).endpoint, subscription(3).endpoint]);
    expect(await count()).toBe(2);
  });

  it("does nothing until VAPID keys are configured", async () => {
    await call(subscribe, testEnv(db), request(doug, "PUT", "/", subscription(2)));
    const sent = await notify(testEnv(db), [{ kind: "invited", challengeId: "c".repeat(22), title: "x", byNickname: "Gaye", userIds: [doug.id] }], (async () => {
      throw new Error("should not send");
    }) as unknown as typeof fetch);
    expect(sent).toBe(0);
  });
});

async function count(): Promise<number> {
  return (await db.prepare("SELECT COUNT(*) AS n FROM push_subscriptions").first<{ n: number }>())?.n ?? 0;
}
