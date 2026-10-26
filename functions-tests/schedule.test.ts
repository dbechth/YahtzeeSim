import { beforeEach, describe, expect, it } from "vitest";
import { base64Url } from "../functions/_lib/crypto";
import { onRequestPost as create } from "../functions/api/challenges/index";
import { onRequestPut as subscribe } from "../functions/api/push/subscription";
import { onRequestPost as submitScore } from "../functions/api/scores";
import { runChallengeSchedule } from "../workers/challenge-cron/src/index";
import { addUser, call, createTestDb, request, testEnv, type TestUser } from "./d1";

const HOUR = 3_600_000;
const p256dh = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
const auth = "BTBZMqHH6r4Tts7J_aSIgg";

let db: D1Database;
let gaye: TestUser;
let doug: TestUser;
let env: ReturnType<typeof testEnv>;
let pushed: string[];
const fetchFn = (async (url: string) => {
  pushed.push(url);
  return new Response(null, { status: 201 });
}) as unknown as typeof fetch;

beforeEach(async () => {
  db = createTestDb();
  gaye = await addUser(db, "Grandma Gaye");
  doug = await addUser(db, "Doug");
  const pair = (await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign"])) as CryptoKeyPair;
  env = testEnv(db, {
    VAPID_PUBLIC_KEY: base64Url(new Uint8Array((await crypto.subtle.exportKey("raw", pair.publicKey)) as ArrayBuffer)),
    VAPID_PRIVATE_KEY: ((await crypto.subtle.exportKey("jwk", pair.privateKey)) as JsonWebKey).d!,
    VAPID_SUBJECT: "mailto:owner@x.test",
  });
  pushed = [];
  for (const [user, n] of [[gaye, 1], [doug, 2]] as const) {
    await call(subscribe, env, request(user, "PUT", "/", { endpoint: `https://push.example.test/device-${n}`, p256dh, auth }));
  }
});

describe("challenge schedule", () => {
  it("reminds players who have not played once, then closes the challenge with results", async () => {
    const now = Date.now();
    const created = await call(
      create,
      testEnv(db),
      request(gaye, "POST", "/api/challenges", { title: "Late night", mode: "Single", deadline: now + 24 * HOUR, nicknames: ["Doug"] }),
    );
    const id = String(created.body.id);
    await call(submitScore, testEnv(db), request(gaye, "POST", "/api/scores", { gameId: crypto.randomUUID(), mode: "Single", total: 220, challengeId: id }));

    expect(await runChallengeSchedule(env, now + 10 * HOUR, fetchFn)).toEqual({ closed: 0, reminded: 0 });
    expect(await runChallengeSchedule(env, now + 19 * HOUR, fetchFn)).toEqual({ closed: 0, reminded: 1 });
    expect(pushed).toEqual(["https://push.example.test/device-2"]);
    expect(await runChallengeSchedule(env, now + 20 * HOUR, fetchFn)).toEqual({ closed: 0, reminded: 0 });

    pushed = [];
    expect(await runChallengeSchedule(env, now + 25 * HOUR, fetchFn)).toEqual({ closed: 1, reminded: 0 });
    expect(pushed.sort()).toEqual(["https://push.example.test/device-1", "https://push.example.test/device-2"]);
  });

  it("does not remind about a challenge created with little time left", async () => {
    const now = Date.now();
    await call(
      create,
      testEnv(db),
      request(gaye, "POST", "/api/challenges", { title: "Quick one", mode: "Single", deadline: now + 2 * HOUR, nicknames: ["Doug"] }),
    );
    expect(await runChallengeSchedule(env, now + HOUR, fetchFn)).toEqual({ closed: 0, reminded: 0 });
  });
});
