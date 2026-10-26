import { beforeEach, describe, expect, it } from "vitest";
import { addMember, decideRequest, GROUP_MAX_MEMBERS, requestToJoin } from "../functions/_lib/groups";
import { messageFor } from "../functions/_lib/notify";
import { onRequestGet as getChallenges, onRequestPost as createChallenge } from "../functions/api/challenges/index";
import { onRequestGet as getChallenge } from "../functions/api/challenges/[id]";
import { onRequestPost as inviteToChallenge } from "../functions/api/challenges/[id]/invite";
import { onRequestGet as getGroup } from "../functions/api/groups/[id]";
import { onRequestPost as join } from "../functions/api/groups/[id]/join";
import { onRequestPost as addToGroup } from "../functions/api/groups/[id]/members";
import { onRequestPost as leave } from "../functions/api/groups/[id]/leave";
import { onRequestPost as remove } from "../functions/api/groups/[id]/remove";
import { onRequestPost as decide } from "../functions/api/groups/[id]/requests";
import { onRequestPost as setRole } from "../functions/api/groups/[id]/role";
import { onRequestGet as listGroups, onRequestPost as createGroup } from "../functions/api/groups/index";
import { onRequestGet as searchGroups } from "../functions/api/groups/search";
import { addUser, call, createTestDb, request, testEnv, type TestUser } from "./d1";

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

const env = () => testEnv(db);

async function newGroup(by: TestUser, name = "Family") {
  return call(createGroup, env(), request(by, "POST", "/api/groups", { name }));
}

const post = (handler: Parameters<typeof call>[0], user: TestUser | null, id: string, body: unknown = {}) =>
  call(handler, env(), request(user, "POST", "/", body), { id });

const detail = (user: TestUser | null, id: string) => call(getGroup, env(), request(user, "GET", "/"), { id });

/** A group with Gaye as admin and Doug as an approved member. */
async function familyWithDoug(): Promise<string> {
  const id = String((await newGroup(gaye)).body.id);
  await post(join, doug, id);
  await post(decide, gaye, id, { nickname: "Doug", approve: true });
  return id;
}

describe("creating and finding groups", () => {
  it("makes the creator its admin and only member", async () => {
    const created = await newGroup(gaye);
    expect(created.status).toBe(201);
    const view = await detail(gaye, String(created.body.id));
    expect(view.body.myRole).toBe("admin");
    expect(view.body.members).toEqual([{ nickname: "Grandma Gaye", role: "admin", isMe: true }]);
  });

  it.each([
    ["no name", "  ", "invalid_name"],
    ["a name that is too long", "x".repeat(31), "invalid_name"],
    ["odd characters", "<script>", "invalid_name"],
  ])("rejects %s", async (_what, name, error) => {
    const created = await newGroup(gaye, name);
    expect(created.status).toBe(400);
    expect(created.body.error).toBe(error);
  });

  it("does not allow two groups with the same name in any letter case", async () => {
    await newGroup(gaye, "Family");
    const again = await newGroup(doug, "  family ");
    expect(again.status).toBe(409);
    expect(again.body.error).toBe("name_taken");
  });

  it("limits how many groups one player can create per day", async () => {
    for (let i = 0; i < 5; i++) {
      expect((await newGroup(gaye, `Group ${i}`)).status).toBe(201);
    }
    expect((await newGroup(gaye, "One more")).status).toBe(429);
  });

  it("requires sign-in and a same-origin JSON request", async () => {
    expect((await newGroup(null as unknown as TestUser)).status).toBe(401);
    const crossSite = new Request("https://x.test/api/groups", {
      method: "POST",
      headers: { Cookie: `yz_session=${gaye.token}`, "Content-Type": "application/json", Origin: "https://evil.test" },
      body: JSON.stringify({ name: "Family" }),
    });
    expect((await call(createGroup, env(), crossSite)).status).toBe(400);
  });

  it("finds groups by part of the name and shows the searcher's own status", async () => {
    const id = String((await newGroup(gaye, "Sunday Family")).body.id);
    await newGroup(gaye, "Work friends");
    await post(join, doug, id);

    const asDoug = await call(searchGroups, env(), request(doug, "GET", "/api/groups/search?q=fam"));
    expect(asDoug.body.groups).toEqual([{ id, name: "Sunday Family", members: 1, membership: "pending" }]);
    const asGaye = await call(searchGroups, env(), request(gaye, "GET", "/api/groups/search?q=FAM"));
    expect((asGaye.body.groups as { membership: string }[])[0].membership).toBe("admin");
    const asStranger = await call(searchGroups, env(), request(stranger, "GET", "/api/groups/search?q=family"));
    expect((asStranger.body.groups as { membership: string }[])[0].membership).toBe("none");

    expect((await call(searchGroups, env(), request(doug, "GET", "/api/groups/search?q="))).body.groups).toEqual([]);
    expect((await call(searchGroups, env(), request(doug, "GET", "/api/groups/search?q=%25"))).body.groups).toEqual([]);
    expect((await call(searchGroups, env(), request(null, "GET", "/api/groups/search?q=a"))).status).toBe(401);
  });
});

describe("joining with approval", () => {
  it("keeps a new player out until an admin approves", async () => {
    const id = String((await newGroup(gaye)).body.id);
    const asked = await post(join, doug, id);
    expect(asked.body.status).toBe("pending");

    expect((await detail(doug, id)).status).toBe(404);
    const mine = await call(listGroups, env(), request(doug, "GET", "/"));
    expect(mine.body.groups).toEqual([{ id, name: "Family", status: "pending", role: "member", members: 1, requests: 0 }]);

    const adminView = await detail(gaye, id);
    expect(adminView.body.requests).toEqual(["Doug"]);
    expect((adminView.body.members as unknown[]).length).toBe(1);

    expect((await post(decide, gaye, id, { nickname: "Doug", approve: true })).status).toBe(200);
    const after = await detail(doug, id);
    expect(after.status).toBe(200);
    expect(after.body.myRole).toBe("member");
    expect((after.body.members as { nickname: string }[]).map((m) => m.nickname)).toEqual(["Grandma Gaye", "Doug"]);
    expect((await detail(gaye, id)).body.requests).toEqual([]);
  });

  it("is idempotent when asking again or when already a member", async () => {
    const id = await familyWithDoug();
    expect((await post(join, doug, id)).body.status).toBe("member");
    await post(join, nicky, id);
    expect((await post(join, nicky, id)).body.status).toBe("pending");
    expect((await detail(gaye, id)).body.requests).toEqual(["Nicky"]);
  });

  it("lets an admin decline and the player ask again later", async () => {
    const id = String((await newGroup(gaye)).body.id);
    await post(join, doug, id);
    expect((await post(decide, gaye, id, { nickname: "Doug", approve: false })).status).toBe(200);
    expect((await detail(doug, id)).status).toBe(404);
    expect((await detail(gaye, id)).body.requests).toEqual([]);
    expect((await post(join, doug, id)).body.status).toBe("pending");
  });

  it("only lets admins decide requests", async () => {
    const id = await familyWithDoug();
    await post(join, nicky, id);
    const byMember = await post(decide, doug, id, { nickname: "Nicky", approve: true });
    expect(byMember.status).toBe(403);
    const byOutsider = await post(decide, stranger, id, { nickname: "Nicky", approve: true });
    expect(byOutsider.status).toBe(404);
    const byApplicant = await post(decide, nicky, id, { nickname: "Nicky", approve: true });
    expect(byApplicant.status).toBe(404);
    expect((await detail(nicky, id)).status).toBe(404);
  });

  it("does not approve someone who did not ask, or someone already in", async () => {
    const id = await familyWithDoug();
    expect((await post(decide, gaye, id, { nickname: "Stranger", approve: true })).status).toBe(404);
    expect((await post(decide, gaye, id, { nickname: "Doug", approve: true })).status).toBe(404);
    expect((await post(decide, gaye, id, { nickname: "Nobody", approve: true })).status).toBe(404);
    expect((await post(decide, gaye, id, { nickname: "Doug" })).status).toBe(400);
  });

  it("rejects requests to join a group that does not exist", async () => {
    expect((await post(join, doug, "A".repeat(22))).status).toBe(404);
    expect((await post(join, doug, "nope")).status).toBe(404);
  });

  it("stops at the member limit when asking and when approving", async () => {
    const id = String((await newGroup(gaye)).body.id);
    const waiting: TestUser[] = [];
    for (let i = 0; i < GROUP_MAX_MEMBERS; i++) {
      const player = await addUser(db, `Player${i}`);
      await post(join, player, id);
      waiting.push(player);
    }
    for (const player of waiting.slice(0, GROUP_MAX_MEMBERS - 1)) {
      expect((await post(decide, gaye, id, { nickname: player.nickname, approve: true })).status).toBe(200);
    }
    const last = waiting[GROUP_MAX_MEMBERS - 1];
    const full = await post(decide, gaye, id, { nickname: last.nickname, approve: true });
    expect(full.status).toBe(409);
    expect(full.body.error).toBe("group_full");
    expect((await post(join, stranger, id)).body.error).toBe("group_full");
  });

  it("limits how many requests one player can have waiting", async () => {
    for (let i = 0; i < 10; i++) {
      const owner = await addUser(db, `Owner${i}`);
      const id = String((await newGroup(owner, `Club ${i}`)).body.id);
      expect((await post(join, stranger, id)).status).toBe(200);
    }
    const owner = await addUser(db, "Owner11");
    const id = String((await newGroup(owner, "Club 11")).body.id);
    expect((await post(join, stranger, id)).status).toBe(429);
  });

  it("notifies the admins of a request and the player when approved", async () => {
    const id = await familyWithDoug();
    await post(decide, gaye, id, { nickname: "Doug", approve: true }).catch(() => null);
    const events: unknown[] = [];
    const { requestToJoin, decideRequest } = await import("../functions/_lib/groups");
    const asked = await requestToJoin(db, { id: nicky.id, nickname: "Nicky" }, id, Date.now());
    if (asked.ok && asked.event) {
      events.push(asked.event);
    }
    const approved = await decideRequest(db, { id: gaye.id }, id, "Nicky", true, Date.now());
    if (approved.ok && approved.event) {
      events.push(approved.event);
    }

    expect(events).toEqual([
      { kind: "group_request", groupId: id, name: "Family", nickname: "Nicky", userIds: [gaye.id] },
      { kind: "group_approved", groupId: id, name: "Family", userIds: [nicky.id] },
    ]);
    expect(messageFor(events[0] as never).body).toBe("Nicky asked to join Tap to approve");
    expect(messageFor(events[1] as never).url).toBe(`/?group=${id}`);
  });
});

describe("group admins", () => {
  it("lets an admin make another member an admin, who can then approve people", async () => {
    const id = await familyWithDoug();
    expect((await post(setRole, doug, id, { nickname: "Doug", role: "admin" })).status).toBe(403);
    expect((await post(setRole, gaye, id, { nickname: "Doug", role: "admin" })).status).toBe(200);
    expect((await detail(doug, id)).body.myRole).toBe("admin");

    await post(join, nicky, id);
    expect((await post(decide, doug, id, { nickname: "Nicky", approve: true })).status).toBe(200);
    const members = (await detail(gaye, id)).body.members as { nickname: string; role: string }[];
    expect(members.map((m) => `${m.nickname}:${m.role}`)).toEqual(["Doug:admin", "Grandma Gaye:admin", "Nicky:member"]);
  });

  it("only promotes active members", async () => {
    const id = String((await newGroup(gaye)).body.id);
    await post(join, doug, id);
    expect((await post(setRole, gaye, id, { nickname: "Doug", role: "admin" })).status).toBe(404);
    expect((await post(setRole, gaye, id, { nickname: "Stranger", role: "admin" })).status).toBe(404);
    expect((await post(setRole, gaye, id, { nickname: "Doug", role: "owner" })).status).toBe(400);
    expect((await detail(gaye, id)).body.requests).toEqual(["Doug"]);
  });

  it("never leaves a group without an admin", async () => {
    const id = await familyWithDoug();
    const only = await post(setRole, gaye, id, { nickname: "Grandma Gaye", role: "member" });
    expect(only.status).toBe(409);
    expect(only.body.error).toBe("last_admin");

    await post(setRole, gaye, id, { nickname: "Doug", role: "admin" });
    expect((await post(setRole, gaye, id, { nickname: "Grandma Gaye", role: "member" })).status).toBe(200);
    expect((await post(setRole, doug, id, { nickname: "Doug", role: "member" })).body.error).toBe("last_admin");
    expect((await detail(doug, id)).body.myRole).toBe("admin");
  });

  it("lets an admin remove a member but not themselves", async () => {
    const id = await familyWithDoug();
    expect((await post(remove, doug, id, { nickname: "Grandma Gaye" })).status).toBe(403);
    expect((await post(remove, gaye, id, { nickname: "Grandma Gaye" })).body.error).toBe("cannot_remove_self");
    expect((await post(remove, gaye, id, { nickname: "Doug" })).status).toBe(200);
    expect((await detail(doug, id)).status).toBe(404);
    expect((await post(remove, gaye, id, { nickname: "Doug" })).status).toBe(404);
  });

  it("lets members leave, and requires another admin before the last admin leaves", async () => {
    const id = await familyWithDoug();
    expect((await post(leave, gaye, id)).body.error).toBe("last_admin");
    expect((await post(leave, doug, id)).status).toBe(200);
    expect((await detail(doug, id)).status).toBe(404);

    await post(join, nicky, id);
    expect((await post(leave, nicky, id)).status).toBe(200);
    expect((await detail(gaye, id)).body.requests).toEqual([]);

    expect((await post(leave, gaye, id)).status).toBe(200);
    expect((await detail(gaye, id)).status).toBe(404);
    expect((await newGroup(doug, "Family")).status).toBe(201);
  });

  it("shows the join-request count to admins only", async () => {
    const id = await familyWithDoug();
    await post(join, nicky, id);
    const asAdmin = await call(listGroups, env(), request(gaye, "GET", "/"));
    expect((asAdmin.body.groups as { requests: number }[])[0].requests).toBe(1);
    const asMember = await call(listGroups, env(), request(doug, "GET", "/"));
    expect((asMember.body.groups as { requests: number }[])[0].requests).toBe(0);

    expect((await call(getChallenges, env(), request(gaye, "GET", "/"))).body.pendingRequests).toBe(1);
    expect((await call(getChallenges, env(), request(doug, "GET", "/"))).body.pendingRequests).toBe(0);
  });
});

describe("inviting groups to a challenge", () => {
  const challenge = (by: TestUser, extra: Record<string, unknown>) =>
    call(createChallenge, env(), request(by, "POST", "/api/challenges", { title: "Sunday showdown", mode: "FullSheet", deadline: Date.now() + 86_400_000, ...extra }));

  const players = async (user: TestUser, id: string) =>
    ((await call(getChallenge, env(), request(user, "GET", "/"), { id })).body.players as { nickname: string }[]).map((p) => p.nickname).sort();

  it("adds every member when creating a challenge", async () => {
    const id = await familyWithDoug();
    await post(join, nicky, id);
    await post(decide, gaye, id, { nickname: "Nicky", approve: true });

    const created = await challenge(gaye, { groupIds: [id], nicknames: ["Doug", "Stranger"] });
    expect(created.status).toBe(201);
    expect(await players(gaye, String(created.body.id))).toEqual(["Doug", "Grandma Gaye", "Nicky", "Stranger"]);
  });

  it("adds every member to an existing challenge and skips those already in it", async () => {
    const id = await familyWithDoug();
    const created = await challenge(doug, { nicknames: ["Stranger"] });
    const challengeId = String(created.body.id);

    const invited = await call(inviteToChallenge, env(), request(doug, "POST", "/", { groupIds: [id] }), { id: challengeId });
    expect(invited.status).toBe(200);
    expect(invited.body.added).toEqual(["Grandma Gaye"]);
    expect(await players(doug, challengeId)).toEqual(["Doug", "Grandma Gaye", "Stranger"]);
  });

  it("does not invite members who only asked to join or were removed", async () => {
    const id = await familyWithDoug();
    await post(join, nicky, id);
    await post(remove, gaye, id, { nickname: "Doug" });
    const created = await challenge(gaye, { groupIds: [id] });
    expect(await players(gaye, String(created.body.id))).toEqual(["Grandma Gaye"]);
  });

  it("only lets a group's own members invite it", async () => {
    const id = await familyWithDoug();
    await post(join, nicky, id);

    for (const outsider of [stranger, nicky]) {
      const created = await challenge(outsider, { groupIds: [id] });
      expect(created.status).toBe(404);
      expect(created.body.error).toBe("group_not_found");
    }

    const open = await challenge(stranger, {});
    const invited = await call(inviteToChallenge, env(), request(stranger, "POST", "/", { groupIds: [id] }), { id: String(open.body.id) });
    expect(invited.status).toBe(404);
    expect(await players(stranger, String(open.body.id))).toEqual(["Stranger"]);
  });

  it("rejects bad group ids and an invite with nobody in it", async () => {
    expect((await challenge(gaye, { groupIds: ["bad"] })).body.error).toBe("invalid_groups");
    expect((await challenge(gaye, { groupIds: "x" })).body.error).toBe("invalid_groups");
    const open = await challenge(gaye, {});
    const empty = await call(inviteToChallenge, env(), request(gaye, "POST", "/", {}), { id: String(open.body.id) });
    expect(empty.status).toBe(400);
  });

  it("refuses a group that would take the challenge over 20 players", async () => {
    const id = await familyWithDoug();
    for (let i = 0; i < GROUP_MAX_MEMBERS - 2; i++) {
      const player = await addUser(db, `Extra${i}`);
      await post(join, player, id);
      await post(decide, gaye, id, { nickname: player.nickname, approve: true });
    }
    const extras = await Promise.all(Array.from({ length: 3 }, (_, i) => addUser(db, `Guest${i}`)));
    const tooMany = await challenge(gaye, { groupIds: [id], nicknames: extras.map((e) => e.nickname) });
    expect(tooMany.status).toBe(400);
    expect(tooMany.body.error).toBe("too_many_players");

    const created = await challenge(gaye, { groupIds: [id] });
    expect(created.status).toBe(201);
    const over = await call(inviteToChallenge, env(), request(gaye, "POST", "/", { nicknames: [extras[0].nickname] }), { id: String(created.body.id) });
    expect(over.body.error).toBe("too_many_players");
  });
});

describe("admins adding players", () => {
  it("puts a player straight in and tells them, with no approval needed", async () => {
    const id = String((await newGroup(gaye)).body.id);
    const added = await post(addToGroup, gaye, id, { nickname: "nicky" });
    expect(added.status).toBe(200);
    expect(added.body).toEqual({ ok: true, added: true });
    expect((await detail(nicky, id)).body.myRole).toBe("member");

    const result = await addMember(db, { id: gaye.id }, id, "Doug", Date.now());
    expect(result.ok && result.event).toEqual({ kind: "group_approved", groupId: id, name: "Family", userIds: [doug.id] });
  });

  it("accepts a waiting request, and does nothing for someone already in", async () => {
    const id = await familyWithDoug();
    await post(join, nicky, id);
    expect((await post(addToGroup, gaye, id, { nickname: "Nicky" })).body.added).toBe(true);
    expect((await detail(gaye, id)).body.requests).toEqual([]);
    expect((await post(addToGroup, gaye, id, { nickname: "Doug" })).body).toEqual({ ok: true, added: false });
  });

  it("only lets admins add, and only players that exist", async () => {
    const id = await familyWithDoug();
    expect((await post(addToGroup, doug, id, { nickname: "Nicky" })).status).toBe(403);
    expect((await post(addToGroup, stranger, id, { nickname: "Nicky" })).status).toBe(404);
    expect((await post(addToGroup, gaye, id, { nickname: "Nobody" })).body.error).toBe("user_not_found");
    expect((await post(addToGroup, gaye, id, {})).status).toBe(400);
    expect((await post(addToGroup, null, id, { nickname: "Nicky" })).status).toBe(401);
    expect((await detail(nicky, id)).status).toBe(404);
  });

  it("stops at the member limit", async () => {
    const id = String((await newGroup(gaye)).body.id);
    for (let i = 0; i < GROUP_MAX_MEMBERS - 1; i++) {
      const player = await addUser(db, `Player${i}`);
      expect((await post(addToGroup, gaye, id, { nickname: player.nickname })).status).toBe(200);
    }
    const full = await post(addToGroup, gaye, id, { nickname: "Stranger" });
    expect(full.status).toBe(409);
    expect(full.body.error).toBe("group_full");
  });
});
