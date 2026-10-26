import { beforeEach, describe, expect, it } from "vitest";
import { CHALLENGE_MAX_PLAYERS, closeExpiredChallenges, dueReminders, ENTRY_GRACE_MS, recordEntry } from "../functions/_lib/challenges";
import { messageFor } from "../functions/_lib/notify";
import { onRequestGet as getDetail } from "../functions/api/challenges/[id]";
import { onRequestPost as invite } from "../functions/api/challenges/[id]/invite";
import { onRequestPost as leave } from "../functions/api/challenges/[id]/leave";
import { onRequestPost as seen } from "../functions/api/challenges/[id]/seen";
import { onRequestGet as list, onRequestPost as create } from "../functions/api/challenges/index";
import { onRequestPost as join } from "../functions/api/challenges/join";
import { onRequestGet as players } from "../functions/api/players";
import { onRequestPost as submitScore } from "../functions/api/scores";
import { addUser, call, createTestDb, request, testEnv, type TestUser } from "./d1";

const HOUR = 3_600_000;

let db: D1Database;
let gaye: TestUser;
let doug: TestUser;
let nicky: TestUser;
let stranger: TestUser;

beforeEach(async () => {
  db = createTestDb();
  gaye = await addUser(db, "Grandma Gaye");
  doug = await addUser(db, "Doug");
  nicky = await addUser(db, "Nicky");
  stranger = await addUser(db, "Stranger");
});

async function newChallenge(by: TestUser, nicknames: string[], extra: Record<string, unknown> = {}) {
  const body = { title: "Sunday showdown", mode: "FullSheet", deadline: Date.now() + 24 * HOUR, nicknames, ...extra };
  return call(create, testEnv(db), request(by, "POST", "/api/challenges", body));
}

const gameId = () => crypto.randomUUID();

async function play(user: TestUser, challengeId: string | undefined, total: number, mode = "FullSheet") {
  return call(submitScore, testEnv(db), request(user, "POST", "/api/scores", { gameId: gameId(), mode, total, challengeId }));
}

describe("creating challenges", () => {
  it("creates a challenge with the creator and the invited players", async () => {
    const created = await newChallenge(gaye, ["doug", "Nicky", "Nobody"]);
    expect(created.status).toBe(201);
    expect(created.body.missing).toEqual(["Nobody"]);
    expect(typeof created.body.inviteToken).toBe("string");

    const detail = await call(getDetail, testEnv(db), request(doug, "GET", "/"), { id: String(created.body.id) });
    expect(detail.status).toBe(200);
    expect((detail.body.players as { nickname: string }[]).map((p) => p.nickname).sort()).toEqual(["Doug", "Grandma Gaye", "Nicky"]);
    expect(detail.body.creator).toBe("Grandma Gaye");
    expect(detail.body.status).toBe("open");
  });

  it.each([
    ["no title", { title: "  " }, "invalid_title"],
    ["a deadline too soon", { deadline: Date.now() + 10 * 60_000 }, "deadline_out_of_range"],
    ["a deadline over a week away", { deadline: Date.now() + 8 * 24 * HOUR }, "deadline_out_of_range"],
    ["an unknown mode", { mode: "Triple" }, "invalid_mode"],
    ["too many players", { nicknames: Array.from({ length: CHALLENGE_MAX_PLAYERS }, (_, i) => `p${i}`) }, "too_many_players"],
  ])("rejects %s", async (_name, extra, error) => {
    const created = await newChallenge(gaye, ["Doug"], extra);
    expect(created.status).toBe(400);
    expect(created.body.error).toBe(error);
  });

  it("limits how many challenges one player can create per day", async () => {
    for (let i = 0; i < 10; i++) {
      expect((await newChallenge(gaye, [])).status).toBe(201);
    }
    expect((await newChallenge(gaye, [])).status).toBe(429);
  });

  it("requires sign-in and a same-origin JSON request", async () => {
    expect((await newChallenge(null as unknown as TestUser, [])).status).toBe(401);
    const crossSite = new Request("https://x.test/api/challenges", {
      method: "POST",
      headers: { Cookie: `yz_session=${gaye.token}`, "Content-Type": "application/json", Origin: "https://evil.test" },
      body: "{}",
    });
    expect((await call(create, testEnv(db), crossSite)).status).toBe(400);
  });
});

describe("privacy", () => {
  it("hides a challenge from everyone who is not in it", async () => {
    const created = await newChallenge(gaye, ["Doug"]);
    const id = String(created.body.id);
    expect((await call(getDetail, testEnv(db), request(stranger, "GET", "/"), { id })).status).toBe(404);
    expect((await call(getDetail, testEnv(db), request(null, "GET", "/"), { id })).status).toBe(401);
    expect(((await call(list, testEnv(db), request(stranger, "GET", "/"))).body.challenges as unknown[]).length).toBe(0);
  });

  it("stops showing a challenge once the player leaves it", async () => {
    const id = String((await newChallenge(gaye, ["Doug"])).body.id);
    expect((await call(leave, testEnv(db), request(doug, "POST", "/", {}), { id })).status).toBe(200);
    expect((await call(getDetail, testEnv(db), request(doug, "GET", "/"), { id })).status).toBe(404);
    expect((await call(leave, testEnv(db), request(doug, "POST", "/", {}), { id })).status).toBe(404);
  });
});

describe("invite links", () => {
  it("lets anyone with the link join while it is open, once", async () => {
    const created = await newChallenge(gaye, []);
    const token = String(created.body.inviteToken);
    const joined = await call(join, testEnv(db), request(stranger, "POST", "/api/challenges/join", { token }));
    expect(joined.status).toBe(200);
    expect(joined.body.id).toBe(created.body.id);
    expect((await call(join, testEnv(db), request(stranger, "POST", "/", { token }))).status).toBe(200);
    const detail = await call(getDetail, testEnv(db), request(stranger, "GET", "/"), { id: String(created.body.id) });
    expect((detail.body.players as unknown[]).length).toBe(2);
    expect(detail.body.inviteToken).toBe(token);
  });

  it("rejects unknown tokens and finished challenges", async () => {
    expect((await call(join, testEnv(db), request(stranger, "POST", "/", { token: "x".repeat(32) }))).status).toBe(404);
    const created = await newChallenge(gaye, []);
    await play(gaye, String(created.body.id), 1500);
    const late = await call(join, testEnv(db), request(stranger, "POST", "/", { token: created.body.inviteToken }));
    expect(late.status).toBe(409);
    expect(late.body.error).toBe("challenge_finished");
  });

  it("lets players invite more people by nickname", async () => {
    const id = String((await newChallenge(gaye, ["Doug"])).body.id);
    const added = await call(invite, testEnv(db), request(doug, "POST", "/", { nicknames: ["Nicky", "Ghost"] }), { id });
    expect(added.body).toEqual({ added: ["Nicky"], missing: ["Ghost"] });
    expect((await call(invite, testEnv(db), request(stranger, "POST", "/", { nicknames: ["Nicky"] }), { id })).status).toBe(404);
  });
});

describe("entries", () => {
  it("records a challenge game as a normal score and as the player's entry", async () => {
    const id = String((await newChallenge(gaye, ["Doug"])).body.id);
    const first = await play(doug, id, 1400);
    expect(first.body).toEqual({ accepted: true, challenge: { recorded: true, reason: null } });
    const scores = await db.prepare("SELECT COUNT(*) AS n FROM scores WHERE user_id = ?1").bind(doug.id).first<{ n: number }>();
    expect(scores?.n).toBe(1);

    // Only the first finished game counts; later ones are still normal scores.
    const second = await play(doug, id, 1700);
    expect(second.body.challenge).toEqual({ recorded: false, reason: "already_played" });
    const detail = await call(getDetail, testEnv(db), request(gaye, "GET", "/"), { id });
    expect((detail.body.players as { nickname: string; total: number | null }[]).find((p) => p.nickname === "Doug")!.total).toBe(1400);
  });

  it("ignores games from other modes and from non-players", async () => {
    const id = String((await newChallenge(gaye, ["Doug"])).body.id);
    expect((await play(doug, id, 150, "Single")).body.challenge).toEqual({ recorded: false, reason: "wrong_mode" });
    expect((await play(stranger, id, 1500)).body.challenge).toEqual({ recorded: false, reason: "not_a_player" });
    expect((await play(stranger, id, 1500)).body.accepted).toBe(true);
  });

  it("finishes early once everyone has played, ranking by score", async () => {
    const id = String((await newChallenge(gaye, ["Doug", "Nicky"])).body.id);
    await play(gaye, id, 1700);
    await play(nicky, id, 1700);
    let detail = await call(getDetail, testEnv(db), request(doug, "GET", "/"), { id });
    expect(detail.body.status).toBe("open");
    await play(doug, id, 1365);
    detail = await call(getDetail, testEnv(db), request(doug, "GET", "/"), { id });
    expect(detail.body.status).toBe("finished");
    expect(detail.body.inviteToken).toBeNull();
    const ranked = detail.body.players as { nickname: string; rank: number }[];
    expect(ranked.map((p) => [p.nickname, p.rank])).toEqual([
      ["Grandma Gaye", 1],
      ["Nicky", 1],
      ["Doug", 3],
    ]);
  });

  it("accepts entries shortly after the deadline, not later", async () => {
    const now = Date.now();
    const id = String((await newChallenge(gaye, ["Doug"], { deadline: now + 2 * HOUR })).body.id);
    const game = { gameId: gameId(), mode: "FullSheet" as const, total: 1300 };
    const late = await recordEntry(db, { id: doug.id, nickname: "Doug" }, id, game, now + 2 * HOUR + ENTRY_GRACE_MS + 1);
    expect(late.reason).toBe("challenge_finished");
    const justInTime = await recordEntry(db, { id: doug.id, nickname: "Doug" }, id, game, now + 2 * HOUR + ENTRY_GRACE_MS - 1);
    expect(justInTime.recorded).toBe(true);
  });

  it("announces entries to the other players and results to everyone", async () => {
    const id = String((await newChallenge(gaye, ["Doug"])).body.id);
    const first = await recordEntry(db, { id: gaye.id, nickname: "Grandma Gaye" }, id, { gameId: gameId(), mode: "FullSheet", total: 1725 }, Date.now());
    expect(first.events.map((e) => [e.kind, e.userIds])).toEqual([["entry", [doug.id]]]);
    const last = await recordEntry(db, { id: doug.id, nickname: "Doug" }, id, { gameId: gameId(), mode: "FullSheet", total: 1365 }, Date.now());
    expect(last.events.map((e) => e.kind)).toEqual(["entry", "results"]);
    const results = last.events[1];
    expect(results.kind === "results" && results.winner).toBe("Grandma Gaye");
    expect(new Set(results.userIds)).toEqual(new Set([gaye.id, doug.id]));
  });
});

describe("list and unread", () => {
  it("shows open challenges with an unread dot until seen", async () => {
    const id = String((await newChallenge(gaye, ["Doug"])).body.id);
    let mine = (await call(list, testEnv(db), request(doug, "GET", "/"))).body.challenges as { id: string; unread: boolean; players: number }[];
    expect(mine).toHaveLength(1);
    expect(mine[0]).toMatchObject({ id, unread: true, players: 2 });
    expect(((await call(list, testEnv(db), request(gaye, "GET", "/"))).body.challenges as { unread: boolean }[])[0].unread).toBe(false);

    await call(seen, testEnv(db), request(doug, "POST", "/", {}), { id });
    mine = (await call(list, testEnv(db), request(doug, "GET", "/"))).body.challenges as { id: string; unread: boolean; players: number }[];
    expect(mine[0].unread).toBe(false);

    await play(gaye, id, 1500);
    mine = (await call(list, testEnv(db), request(doug, "GET", "/"))).body.challenges as { id: string; unread: boolean; players: number }[];
    expect(mine[0].unread).toBe(true);
  });

  it("searches other players by nickname", async () => {
    const found = await call(players, testEnv(db), request(gaye, "GET", "/api/players?q=ck"));
    expect(found.body.players).toEqual(["Nicky"]);
    const all = await call(players, testEnv(db), request(gaye, "GET", "/api/players?q="));
    expect(all.body.players).toEqual(["Doug", "Nicky", "Stranger"]);
    const wild = await call(players, testEnv(db), request(gaye, "GET", "/api/players?q=%25"));
    expect(wild.body.players).toEqual([]);
  });
});

describe("scheduled work", () => {
  it("closes expired challenges once and announces the results", async () => {
    const now = Date.now();
    const id = String((await newChallenge(gaye, ["Doug"], { deadline: now + 2 * HOUR })).body.id);
    await play(gaye, id, 1600);
    expect(await closeExpiredChallenges(db, now + HOUR)).toEqual([]);
    const events = await closeExpiredChallenges(db, now + 3 * HOUR);
    expect(events).toHaveLength(1);
    expect(events[0]).toMatchObject({ kind: "results", challengeId: id, winner: "Grandma Gaye", winnerTotal: 1600 });
    expect(await closeExpiredChallenges(db, now + 4 * HOUR)).toEqual([]);
  });

  it("reminds only players who have not played, close to the deadline", async () => {
    const now = Date.now();
    const id = String((await newChallenge(gaye, ["Doug", "Nicky"], { deadline: now + 24 * HOUR })).body.id);
    await play(gaye, id, 1600);
    expect(await dueReminders(db, now + 17 * HOUR, 6 * HOUR)).toEqual([]);
    const due = await dueReminders(db, now + 19 * HOUR, 6 * HOUR);
    expect(due).toHaveLength(1);
    expect(new Set(due[0].userIds)).toEqual(new Set([doug.id, nicky.id]));
  });

  it("does not remind about challenges created with less time left than the window", async () => {
    const now = Date.now();
    await newChallenge(gaye, ["Doug"], { deadline: now + 5 * HOUR });
    expect(await dueReminders(db, now, 6 * HOUR)).toEqual([]);
  });

  it("writes notifications without punctuation, like the app", () => {
    const text = [
      messageFor({ kind: "invited", challengeId: "c".repeat(22), title: "Sunday showdown", byNickname: "Gaye", userIds: [] }),
      messageFor({ kind: "entry", challengeId: "c".repeat(22), title: "Sunday showdown", nickname: "Doug", total: 1365, userIds: [] }),
      messageFor({ kind: "results", challengeId: "c".repeat(22), title: "Sunday showdown", winner: "Gaye", winnerTotal: 1725, userIds: [] }),
    ];
    expect(text[1]).toMatchObject({ title: "Sunday showdown", body: "Doug scored 1365 Can you beat it", url: "/?challenge=" + "c".repeat(22) });
    for (const m of text) {
      expect(m.title + m.body).not.toMatch(/[.,;:!?]/);
    }
  });
});
