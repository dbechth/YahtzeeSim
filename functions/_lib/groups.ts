import { randomToken } from "./crypto";

export const GROUP_MAX_MEMBERS = 20;
export const GROUP_NAME_MAX = 30;
export const GROUPS_CREATED_PER_DAY = 5;
export const GROUP_PENDING_REQUESTS_MAX = 10;
export const GROUPS_PER_INVITE = 5;

const DAY_MS = 24 * 60 * 60 * 1000;
const NAME = /^[\p{L}\p{N}][\p{L}\p{N} .,!?'&:()#-]*$/u;
const GROUP_ID = /^[A-Za-z0-9_-]{16,64}$/;

export type GroupRole = "member" | "admin";

/** Something group members should hear about. Delivered by notify.ts (push). */
export type GroupEvent =
  | { kind: "group_request"; groupId: string; name: string; nickname: string; userIds: string[] }
  | { kind: "group_approved"; groupId: string; name: string; userIds: string[] };

export type GroupError = "not_found" | "user_not_found" | "forbidden" | "name_taken" | "group_full" | "rate_limited" | "too_many_requests" | "last_admin" | "cannot_remove_self";

interface GroupRow {
  id: string;
  name: string;
}

interface Member {
  id: string;
  nickname: string;
}

export function isGroupId(value: unknown): value is string {
  return typeof value === "string" && GROUP_ID.test(value);
}

export function validateGroupName(value: unknown): string | null {
  const name = typeof value === "string" ? value.trim().replace(/\s+/g, " ") : "";
  return name.length > 0 && name.length <= GROUP_NAME_MAX && NAME.test(name) ? name : null;
}

/** Group ids from a request body (missing means none). Null when the value is not a short list of ids. */
export function parseGroupIds(value: unknown): string[] | null {
  if (value === undefined || value === null) {
    return [];
  }
  if (!Array.isArray(value) || value.length > GROUPS_PER_INVITE || value.some((id) => !isGroupId(id))) {
    return null;
  }
  return [...new Set(value as string[])];
}

export async function createGroup(
  db: D1Database,
  creator: { id: string },
  rawName: unknown,
  now: number,
): Promise<{ ok: true; id: string } | { ok: false; error: GroupError | "invalid_name" }> {
  const name = validateGroupName(rawName);
  if (!name) {
    return { ok: false, error: "invalid_name" };
  }
  const recent = await db
    .prepare("SELECT COUNT(*) AS n FROM user_groups WHERE created_by = ?1 AND created_at > ?2")
    .bind(creator.id, now - DAY_MS)
    .first<{ n: number }>();
  if ((recent?.n ?? 0) >= GROUPS_CREATED_PER_DAY) {
    return { ok: false, error: "rate_limited" };
  }

  const id = randomToken(16);
  const inserted = await db
    .prepare("INSERT INTO user_groups (id, name, name_key, created_by, created_at) VALUES (?1, ?2, ?3, ?4, ?5) ON CONFLICT (name_key) DO NOTHING")
    .bind(id, name, name.toLowerCase(), creator.id, now)
    .run();
  if ((inserted.meta?.changes ?? 0) === 0) {
    return { ok: false, error: "name_taken" };
  }
  await db
    .prepare("INSERT INTO group_members (group_id, user_id, role, status, requested_at, joined_at) VALUES (?1, ?2, 'admin', 'active', ?3, ?3)")
    .bind(id, creator.id, now)
    .run();
  return { ok: true, id };
}

export interface GroupSearchResult {
  id: string;
  name: string;
  members: number;
  /** The searching player's relation to the group. */
  membership: "none" | "pending" | "member" | "admin";
}

/** Up to 20 groups whose name contains the text (case-insensitive), alphabetical. */
export async function searchGroups(db: D1Database, userId: string, query: string): Promise<GroupSearchResult[]> {
  const text = query.trim().slice(0, 16);
  if (text.length === 0) {
    return [];
  }
  const rows = await db
    .prepare(
      `SELECT g.id, g.name, m.role, m.status,
              (SELECT COUNT(*) FROM group_members a WHERE a.group_id = g.id AND a.status = 'active') AS members
       FROM user_groups g
       LEFT JOIN group_members m ON m.group_id = g.id AND m.user_id = ?1
       WHERE g.name LIKE ?2 ESCAPE '\\'
       ORDER BY g.name COLLATE NOCASE LIMIT 20`,
    )
    .bind(userId, `%${text.replace(/[%_\\]/g, (c) => "\\" + c)}%`)
    .all<{ id: string; name: string; role: GroupRole | null; status: "pending" | "active" | null; members: number }>();
  return rows.results.map((r) => ({
    id: r.id,
    name: r.name,
    members: r.members,
    membership: r.status === null ? "none" : r.status === "pending" ? "pending" : r.role === "admin" ? "admin" : "member",
  }));
}

/** Asks to join. An admin approves it (decideRequest). Asking again, or when already in, changes nothing. */
export async function requestToJoin(
  db: D1Database,
  user: { id: string; nickname: string },
  groupId: string,
  now: number,
): Promise<{ ok: true; status: "pending" | "member"; event: GroupEvent | null } | { ok: false; error: GroupError }> {
  const group = await getGroup(db, groupId);
  if (!group) {
    return { ok: false, error: "not_found" };
  }
  const existing = await membership(db, user.id, groupId);
  if (existing) {
    return { ok: true, status: existing.status === "active" ? "member" : "pending", event: null };
  }
  if ((await activeCount(db, groupId)) >= GROUP_MAX_MEMBERS) {
    return { ok: false, error: "group_full" };
  }
  const pending = await db
    .prepare("SELECT COUNT(*) AS n FROM group_members WHERE user_id = ?1 AND status = 'pending'")
    .bind(user.id)
    .first<{ n: number }>();
  if ((pending?.n ?? 0) >= GROUP_PENDING_REQUESTS_MAX) {
    return { ok: false, error: "too_many_requests" };
  }

  await db
    .prepare("INSERT INTO group_members (group_id, user_id, role, status, requested_at) VALUES (?1, ?2, 'member', 'pending', ?3)")
    .bind(groupId, user.id, now)
    .run();
  const admins = await db
    .prepare("SELECT user_id FROM group_members WHERE group_id = ?1 AND status = 'active' AND role = 'admin'")
    .bind(groupId)
    .all<{ user_id: string }>();
  return {
    ok: true,
    status: "pending",
    event: { kind: "group_request", groupId, name: group.name, nickname: user.nickname, userIds: admins.results.map((a) => a.user_id) },
  };
}

export interface GroupSummary {
  id: string;
  name: string;
  /** "pending" means the player asked to join and is waiting for an admin. */
  status: "pending" | "active";
  role: GroupRole;
  members: number;
  /** Join requests waiting for approval; only counted for admins. */
  requests: number;
}

/** The player's groups (and the ones they asked to join), alphabetical. */
export async function listGroups(db: D1Database, userId: string): Promise<GroupSummary[]> {
  const rows = await db
    .prepare(
      `SELECT g.id, g.name, m.role, m.status,
              (SELECT COUNT(*) FROM group_members a WHERE a.group_id = g.id AND a.status = 'active') AS members,
              CASE WHEN m.role = 'admin' AND m.status = 'active'
                   THEN (SELECT COUNT(*) FROM group_members p WHERE p.group_id = g.id AND p.status = 'pending') ELSE 0 END AS requests
       FROM group_members m JOIN user_groups g ON g.id = m.group_id
       WHERE m.user_id = ?1
       ORDER BY g.name COLLATE NOCASE LIMIT 100`,
    )
    .bind(userId)
    .all<GroupSummary>();
  return rows.results;
}

/** Join requests waiting for this player to approve, across every group they administer (for the unread dot). */
export async function pendingRequestCount(db: D1Database, userId: string): Promise<number> {
  const row = await db
    .prepare(
      `SELECT COUNT(*) AS n FROM group_members p
       JOIN group_members me ON me.group_id = p.group_id AND me.user_id = ?1 AND me.status = 'active' AND me.role = 'admin'
       WHERE p.status = 'pending'`,
    )
    .bind(userId)
    .first<{ n: number }>();
  return row?.n ?? 0;
}

export interface GroupDetail {
  id: string;
  name: string;
  myRole: GroupRole;
  members: { nickname: string; role: GroupRole; isMe: boolean }[];
  /** Nicknames waiting for approval. Empty unless the viewer is an admin. */
  requests: string[];
}

/** Null unless the user is an active member: the member list is not shown to anyone else. */
export async function getGroupDetail(db: D1Database, userId: string, groupId: string): Promise<GroupDetail | null> {
  const mine = await membership(db, userId, groupId);
  const group = mine?.status === "active" ? await getGroup(db, groupId) : null;
  if (!mine || !group) {
    return null;
  }
  const rows = await db
    .prepare(
      `SELECT m.user_id, u.nickname, m.role, m.status
       FROM group_members m JOIN users u ON u.id = m.user_id
       WHERE m.group_id = ?1 ORDER BY m.role = 'admin' DESC, u.nickname COLLATE NOCASE`,
    )
    .bind(groupId)
    .all<{ user_id: string; nickname: string | null; role: GroupRole; status: "pending" | "active" }>();
  return {
    id: group.id,
    name: group.name,
    myRole: mine.role,
    members: rows.results
      .filter((r) => r.status === "active")
      .map((r) => ({ nickname: r.nickname ?? "", role: r.role, isMe: r.user_id === userId })),
    requests: mine.role === "admin" ? rows.results.filter((r) => r.status === "pending").map((r) => r.nickname ?? "") : [],
  };
}

/** Approves or declines a join request. Group admins only. */
export async function decideRequest(
  db: D1Database,
  admin: { id: string },
  groupId: string,
  nickname: string,
  approve: boolean,
  now: number,
): Promise<{ ok: true; event: GroupEvent | null } | { ok: false; error: GroupError }> {
  const allowed = await requireAdmin(db, admin.id, groupId);
  if (!allowed.ok) {
    return allowed;
  }
  const target = await findPending(db, groupId, nickname);
  if (!target) {
    return { ok: false, error: "not_found" };
  }

  if (!approve) {
    await db.prepare("DELETE FROM group_members WHERE group_id = ?1 AND user_id = ?2 AND status = 'pending'").bind(groupId, target.id).run();
    return { ok: true, event: null };
  }
  // The capacity check is part of the UPDATE so two admins approving at once cannot overfill the group.
  const updated = await db
    .prepare(
      `UPDATE group_members SET status = 'active', joined_at = ?3
       WHERE group_id = ?1 AND user_id = ?2 AND status = 'pending'
         AND (SELECT COUNT(*) FROM group_members WHERE group_id = ?1 AND status = 'active') < ?4`,
    )
    .bind(groupId, target.id, now, GROUP_MAX_MEMBERS)
    .run();
  if ((updated.meta?.changes ?? 0) === 0) {
    return { ok: false, error: "group_full" };
  }
  return { ok: true, event: { kind: "group_approved", groupId, name: allowed.group.name, userIds: [target.id] } };
}

/**
 * An admin adds a player straight into the group by nickname, with no approval step. If the player had asked to join,
 * that request is accepted. `added` is false when they were already a member. The player is notified.
 */
export async function addMember(
  db: D1Database,
  admin: { id: string },
  groupId: string,
  nickname: string,
  now: number,
): Promise<{ ok: true; added: boolean; event: GroupEvent | null } | { ok: false; error: GroupError }> {
  const allowed = await requireAdmin(db, admin.id, groupId);
  if (!allowed.ok) {
    return allowed;
  }
  const target = await db.prepare("SELECT id FROM users WHERE nickname = ?1").bind(nickname).first<{ id: string }>();
  if (!target) {
    return { ok: false, error: "user_not_found" };
  }
  const existing = await membership(db, target.id, groupId);
  if (existing?.status === "active") {
    return { ok: true, added: false, event: null };
  }

  // The capacity check is part of the statement so two admins adding at once cannot overfill the group.
  const result = existing
    ? await db
        .prepare(
          `UPDATE group_members SET status = 'active', joined_at = ?3
           WHERE group_id = ?1 AND user_id = ?2 AND status = 'pending'
             AND (SELECT COUNT(*) FROM group_members WHERE group_id = ?1 AND status = 'active') < ?4`,
        )
        .bind(groupId, target.id, now, GROUP_MAX_MEMBERS)
        .run()
    : await db
        .prepare(
          `INSERT INTO group_members (group_id, user_id, role, status, requested_at, joined_at)
           SELECT ?1, ?2, 'member', 'active', ?3, ?3
           WHERE (SELECT COUNT(*) FROM group_members WHERE group_id = ?1 AND status = 'active') < ?4`,
        )
        .bind(groupId, target.id, now, GROUP_MAX_MEMBERS)
        .run();
  if ((result.meta?.changes ?? 0) === 0) {
    return { ok: false, error: "group_full" };
  }
  return { ok: true, added: true, event: { kind: "group_approved", groupId, name: allowed.group.name, userIds: [target.id] } };
}

/** Makes a member an admin or an admin a plain member again. A group always keeps at least one admin. */
export async function setRole(
  db: D1Database,
  admin: { id: string },
  groupId: string,
  nickname: string,
  role: GroupRole,
): Promise<{ ok: true } | { ok: false; error: GroupError }> {
  const allowed = await requireAdmin(db, admin.id, groupId);
  if (!allowed.ok) {
    return allowed;
  }
  const target = await findActiveMember(db, groupId, nickname);
  if (!target) {
    return { ok: false, error: "not_found" };
  }

  const updated = await db
    .prepare(
      `UPDATE group_members SET role = ?3
       WHERE group_id = ?1 AND user_id = ?2 AND status = 'active'
         AND (?3 = 'admin' OR role <> 'admin'
              OR (SELECT COUNT(*) FROM group_members WHERE group_id = ?1 AND status = 'active' AND role = 'admin') > 1)`,
    )
    .bind(groupId, target.id, role)
    .run();
  return (updated.meta?.changes ?? 0) > 0 ? { ok: true } : { ok: false, error: "last_admin" };
}

/** An admin removes another member. Admins leave with leaveGroup instead. */
export async function removeMember(
  db: D1Database,
  admin: { id: string },
  groupId: string,
  nickname: string,
): Promise<{ ok: true } | { ok: false; error: GroupError }> {
  const allowed = await requireAdmin(db, admin.id, groupId);
  if (!allowed.ok) {
    return allowed;
  }
  const target = await findActiveMember(db, groupId, nickname);
  if (!target) {
    return { ok: false, error: "not_found" };
  }
  if (target.id === admin.id) {
    return { ok: false, error: "cannot_remove_self" };
  }
  await db.prepare("DELETE FROM group_members WHERE group_id = ?1 AND user_id = ?2").bind(groupId, target.id).run();
  return { ok: true };
}

/**
 * Leaves the group, or withdraws a join request. The last admin cannot leave while others remain (make someone else
 * an admin first); when they are the only member the group is deleted.
 */
export async function leaveGroup(db: D1Database, userId: string, groupId: string): Promise<{ ok: true } | { ok: false; error: GroupError }> {
  const mine = await membership(db, userId, groupId);
  if (!mine) {
    return { ok: false, error: "not_found" };
  }
  if (mine.status === "active" && mine.role === "admin") {
    const others = await db
      .prepare("SELECT role FROM group_members WHERE group_id = ?1 AND user_id <> ?2 AND status = 'active'")
      .bind(groupId, userId)
      .all<{ role: GroupRole }>();
    if (others.results.length === 0) {
      await db.prepare("DELETE FROM user_groups WHERE id = ?1").bind(groupId).run();
      return { ok: true };
    }
    if (!others.results.some((o) => o.role === "admin")) {
      return { ok: false, error: "last_admin" };
    }
  }
  await db.prepare("DELETE FROM group_members WHERE group_id = ?1 AND user_id = ?2").bind(groupId, userId).run();
  return { ok: true };
}

/**
 * Everyone in the given groups, for inviting them to a challenge. Null when the player is not an active member of
 * every one of them, so a group can only be invited by its own members.
 */
export async function membersOfGroups(db: D1Database, userId: string, groupIds: string[]): Promise<Member[] | null> {
  if (groupIds.length === 0) {
    return [];
  }
  const placeholders = groupIds.map((_, i) => `?${i + 2}`).join(", ");
  const memberPlaceholders = groupIds.map((_, i) => `?${i + 1}`).join(", ");
  const mine = await db
    .prepare(`SELECT COUNT(*) AS n FROM group_members WHERE user_id = ?1 AND status = 'active' AND group_id IN (${placeholders})`)
    .bind(userId, ...groupIds)
    .first<{ n: number }>();
  if ((mine?.n ?? 0) !== groupIds.length) {
    return null;
  }
  const rows = await db
    .prepare(
      `SELECT DISTINCT u.id, u.nickname FROM group_members m JOIN users u ON u.id = m.user_id
       WHERE m.status = 'active' AND m.group_id IN (${memberPlaceholders})`,
    )
    .bind(...groupIds)
    .all<{ id: string; nickname: string | null }>();
  return rows.results.filter((r): r is Member => r.nickname !== null);
}

async function getGroup(db: D1Database, groupId: string): Promise<GroupRow | null> {
  if (!isGroupId(groupId)) {
    return null;
  }
  return await db.prepare("SELECT id, name FROM user_groups WHERE id = ?1").bind(groupId).first<GroupRow>();
}

async function membership(db: D1Database, userId: string, groupId: string): Promise<{ role: GroupRole; status: "pending" | "active" } | null> {
  if (!isGroupId(groupId)) {
    return null;
  }
  return await db
    .prepare("SELECT role, status FROM group_members WHERE group_id = ?1 AND user_id = ?2")
    .bind(groupId, userId)
    .first<{ role: GroupRole; status: "pending" | "active" }>();
}

/** Non-members (and people who only asked to join) get not_found, so they cannot tell what the group holds. */
async function requireAdmin(
  db: D1Database,
  userId: string,
  groupId: string,
): Promise<{ ok: true; group: GroupRow } | { ok: false; error: "not_found" | "forbidden" }> {
  const mine = await membership(db, userId, groupId);
  const group = mine?.status === "active" ? await getGroup(db, groupId) : null;
  if (!mine || !group) {
    return { ok: false, error: "not_found" };
  }
  return mine.role === "admin" ? { ok: true, group } : { ok: false, error: "forbidden" };
}

async function activeCount(db: D1Database, groupId: string): Promise<number> {
  const row = await db
    .prepare("SELECT COUNT(*) AS n FROM group_members WHERE group_id = ?1 AND status = 'active'")
    .bind(groupId)
    .first<{ n: number }>();
  return row?.n ?? 0;
}

function findByStatus(db: D1Database, groupId: string, nickname: string, status: "pending" | "active") {
  return db
    .prepare(
      `SELECT u.id, u.nickname FROM group_members m JOIN users u ON u.id = m.user_id
       WHERE m.group_id = ?1 AND m.status = ?3 AND u.nickname = ?2`,
    )
    .bind(groupId, nickname, status)
    .first<Member>();
}

const findPending = (db: D1Database, groupId: string, nickname: string) => findByStatus(db, groupId, nickname, "pending");
const findActiveMember = (db: D1Database, groupId: string, nickname: string) => findByStatus(db, groupId, nickname, "active");
