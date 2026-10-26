import { randomToken } from "./crypto";
import { membersOfGroups, parseGroupIds } from "./groups";
import type { GameMode } from "./validate";
import { parseMode } from "./validate";

export const CHALLENGE_MAX_DAYS = 7;
export const CHALLENGE_MIN_MINUTES = 60;
export const CHALLENGE_MAX_PLAYERS = 20;
export const CHALLENGES_PER_DAY = 10;
export const TITLE_MAX = 40;
/** Entries uploaded shortly after the deadline still count, so a game finished offline just in time is not lost. */
export const ENTRY_GRACE_MS = 15 * 60 * 1000;

const DAY_MS = 24 * 60 * 60 * 1000;
const TITLE = /^[\p{L}\p{N}][\p{L}\p{N} .,!?'&:()#-]*$/u;
const CHALLENGE_ID = /^[A-Za-z0-9_-]{16,64}$/;

export interface NewChallenge {
  title: string;
  mode: GameMode;
  deadline: number;
  nicknames: string[];
  /** Groups whose members are all invited; the creator has to be in each one. */
  groupIds: string[];
}

export function isChallengeId(value: unknown): value is string {
  return typeof value === "string" && CHALLENGE_ID.test(value);
}

export function validateNewChallenge(body: unknown, now: number): { ok: true; value: NewChallenge } | { ok: false; error: string } {
  if (typeof body !== "object" || body === null) {
    return { ok: false, error: "invalid_body" };
  }
  const { title, mode, deadline, nicknames, groupIds } = body as Record<string, unknown>;
  const parsedMode = parseMode(mode);
  if (!parsedMode) {
    return { ok: false, error: "invalid_mode" };
  }
  const cleanTitle = typeof title === "string" ? title.trim().replace(/\s+/g, " ") : "";
  if (cleanTitle.length === 0 || cleanTitle.length > TITLE_MAX || !TITLE.test(cleanTitle)) {
    return { ok: false, error: "invalid_title" };
  }
  if (typeof deadline !== "number" || !Number.isInteger(deadline)) {
    return { ok: false, error: "invalid_deadline" };
  }
  if (deadline < now + CHALLENGE_MIN_MINUTES * 60 * 1000 || deadline > now + CHALLENGE_MAX_DAYS * DAY_MS) {
    return { ok: false, error: "deadline_out_of_range" };
  }
  const list = nicknames ?? [];
  if (!Array.isArray(list) || list.some((n) => typeof n !== "string" || n.length > 32)) {
    return { ok: false, error: "invalid_players" };
  }
  const unique = [...new Map((list as string[]).map((n) => [n.trim().toLowerCase(), n.trim()])).values()].filter((n) => n.length > 0);
  if (unique.length > CHALLENGE_MAX_PLAYERS - 1) {
    return { ok: false, error: "too_many_players" };
  }
  const groups = parseGroupIds(groupIds);
  if (!groups) {
    return { ok: false, error: "invalid_groups" };
  }
  return { ok: true, value: { title: cleanTitle, mode: parsedMode, deadline, nicknames: unique, groupIds: groups } };
}

export type ChallengeStatus = "open" | "finished";

export function challengeStatus(row: { deadline: number; finished_at: number | null }, now: number): ChallengeStatus {
  return row.finished_at !== null || now >= row.deadline ? "finished" : "open";
}

/** Something players should hear about. Delivered by notify.ts (push) and always visible in the app. */
export type ChallengeEvent =
  | { kind: "invited"; challengeId: string; title: string; byNickname: string; userIds: string[] }
  | { kind: "entry"; challengeId: string; title: string; nickname: string; total: number; userIds: string[] }
  | { kind: "results"; challengeId: string; title: string; winner: string | null; winnerTotal: number | null; userIds: string[] };

interface ChallengeRow {
  id: string;
  creator_id: string;
  title: string;
  mode: GameMode;
  created_at: number;
  deadline: number;
  invite_token: string;
  updated_at: number;
  finished_at: number | null;
}

export async function createChallenge(
  db: D1Database,
  creator: { id: string; nickname: string },
  input: NewChallenge,
  now: number,
): Promise<{ ok: true; id: string; inviteToken: string; missing: string[]; event: ChallengeEvent | null } | { ok: false; error: string }> {
  const recent = await db
    .prepare("SELECT COUNT(*) AS n FROM challenges WHERE creator_id = ?1 AND created_at > ?2")
    .bind(creator.id, now - DAY_MS)
    .first<{ n: number }>();
  if ((recent?.n ?? 0) >= CHALLENGES_PER_DAY) {
    return { ok: false, error: "rate_limited" };
  }

  const found = await findUsersByNickname(db, input.nicknames);
  const groupMembers = await membersOfGroups(db, creator.id, input.groupIds);
  if (!groupMembers) {
    return { ok: false, error: "group_not_found" };
  }
  const invited = uniqueUsers([...found, ...groupMembers]).filter((u) => u.id !== creator.id);
  const missing = input.nicknames.filter((n) => !found.some((u) => u.nickname.toLowerCase() === n.toLowerCase()));
  if (invited.length + 1 > CHALLENGE_MAX_PLAYERS) {
    return { ok: false, error: "too_many_players" };
  }

  const id = randomToken(16);
  const inviteToken = randomToken(24);
  const statements = [
    db
      .prepare(
        `INSERT INTO challenges (id, creator_id, title, mode, created_at, deadline, invite_token, updated_at)
         VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?5)`,
      )
      .bind(id, creator.id, input.title, input.mode, now, input.deadline, inviteToken),
    db.prepare("INSERT INTO challenge_players (challenge_id, user_id, joined_at, seen_at) VALUES (?1, ?2, ?3, ?3)").bind(id, creator.id, now),
    ...invited.map((u) =>
      db.prepare("INSERT INTO challenge_players (challenge_id, user_id, joined_at) VALUES (?1, ?2, ?3)").bind(id, u.id, now),
    ),
  ];
  await db.batch(statements);

  const event: ChallengeEvent | null =
    invited.length > 0
      ? { kind: "invited", challengeId: id, title: input.title, byNickname: creator.nickname, userIds: invited.map((u) => u.id) }
      : null;
  return { ok: true, id, inviteToken, missing, event };
}

/** Adds more players by nickname to an open challenge. Any of its players may invite. */
export async function invitePlayers(
  db: D1Database,
  inviter: { id: string; nickname: string },
  challengeId: string,
  nicknames: string[],
  groupIds: string[],
  now: number,
): Promise<{ ok: true; added: string[]; missing: string[]; event: ChallengeEvent | null } | { ok: false; error: string }> {
  const challenge = await getChallengeForPlayer(db, inviter.id, challengeId);
  if (!challenge) {
    return { ok: false, error: "not_found" };
  }
  if (challengeStatus(challenge, now) !== "open") {
    return { ok: false, error: "challenge_finished" };
  }

  const players = await activePlayers(db, challengeId);
  const found = await findUsersByNickname(db, nicknames);
  const groupMembers = await membersOfGroups(db, inviter.id, groupIds);
  if (!groupMembers) {
    return { ok: false, error: "group_not_found" };
  }
  const toAdd = uniqueUsers([...found, ...groupMembers]).filter((u) => !players.some((p) => p.user_id === u.id));
  const missing = nicknames.filter((n) => !found.some((u) => u.nickname.toLowerCase() === n.toLowerCase()));
  if (players.length + toAdd.length > CHALLENGE_MAX_PLAYERS) {
    return { ok: false, error: "too_many_players" };
  }
  if (toAdd.length > 0) {
    await db.batch([
      ...toAdd.map((u) =>
        db
          .prepare(
            `INSERT INTO challenge_players (challenge_id, user_id, joined_at) VALUES (?1, ?2, ?3)
             ON CONFLICT (challenge_id, user_id) DO UPDATE SET left_at = NULL, joined_at = excluded.joined_at`,
          )
          .bind(challengeId, u.id, now),
      ),
      db.prepare("UPDATE challenges SET updated_at = ?2 WHERE id = ?1").bind(challengeId, now),
    ]);
  }

  const event: ChallengeEvent | null =
    toAdd.length > 0
      ? { kind: "invited", challengeId, title: challenge.title, byNickname: inviter.nickname, userIds: toAdd.map((u) => u.id) }
      : null;
  return { ok: true, added: toAdd.map((u) => u.nickname), missing, event };
}

/** Joins with an invite link. Works for anyone signed in, while the challenge is open and not full. */
export async function joinByToken(
  db: D1Database,
  userId: string,
  token: unknown,
  now: number,
): Promise<{ ok: true; id: string } | { ok: false; error: string }> {
  if (typeof token !== "string" || token.length < 16 || token.length > 64) {
    return { ok: false, error: "not_found" };
  }
  const challenge = await db.prepare("SELECT * FROM challenges WHERE invite_token = ?1").bind(token).first<ChallengeRow>();
  if (!challenge) {
    return { ok: false, error: "not_found" };
  }
  const existing = await db
    .prepare("SELECT left_at FROM challenge_players WHERE challenge_id = ?1 AND user_id = ?2")
    .bind(challenge.id, userId)
    .first<{ left_at: number | null }>();
  if (existing && existing.left_at === null) {
    return { ok: true, id: challenge.id };
  }
  if (challengeStatus(challenge, now) !== "open") {
    return { ok: false, error: "challenge_finished" };
  }
  const players = await activePlayers(db, challenge.id);
  if (players.length >= CHALLENGE_MAX_PLAYERS) {
    return { ok: false, error: "challenge_full" };
  }
  await db.batch([
    db
      .prepare(
        `INSERT INTO challenge_players (challenge_id, user_id, joined_at) VALUES (?1, ?2, ?3)
         ON CONFLICT (challenge_id, user_id) DO UPDATE SET left_at = NULL, joined_at = excluded.joined_at`,
      )
      .bind(challenge.id, userId, now),
    db.prepare("UPDATE challenges SET updated_at = ?2 WHERE id = ?1").bind(challenge.id, now),
  ]);
  return { ok: true, id: challenge.id };
}

export async function leaveChallenge(db: D1Database, userId: string, challengeId: string, now: number): Promise<boolean> {
  const result = await db
    .prepare("UPDATE challenge_players SET left_at = ?3 WHERE challenge_id = ?1 AND user_id = ?2 AND left_at IS NULL")
    .bind(challengeId, userId, now)
    .run();
  return (result.meta?.changes ?? 0) > 0;
}

export async function markSeen(db: D1Database, userId: string, challengeId: string, now: number): Promise<void> {
  await db
    .prepare("UPDATE challenge_players SET seen_at = ?3 WHERE challenge_id = ?1 AND user_id = ?2 AND left_at IS NULL")
    .bind(challengeId, userId, now)
    .run();
}

export interface ChallengeSummary {
  id: string;
  title: string;
  mode: GameMode;
  creator: string;
  deadline: number;
  status: ChallengeStatus;
  players: number;
  entries: number;
  myTotal: number | null;
  unread: boolean;
}

/** The player's challenges: open ones first (soonest deadline), then the 20 most recent finished ones. */
export async function listChallenges(db: D1Database, userId: string, now: number): Promise<ChallengeSummary[]> {
  const rows = await db
    .prepare(
      `SELECT c.id, c.title, c.mode, c.deadline, c.finished_at, c.updated_at, u.nickname AS creator, p.seen_at,
              (SELECT COUNT(*) FROM challenge_players x WHERE x.challenge_id = c.id AND x.left_at IS NULL) AS players,
              (SELECT COUNT(*) FROM challenge_entries e WHERE e.challenge_id = c.id) AS entries,
              (SELECT total FROM challenge_entries e WHERE e.challenge_id = c.id AND e.user_id = ?1) AS my_total
       FROM challenge_players p
       JOIN challenges c ON c.id = p.challenge_id
       JOIN users u ON u.id = c.creator_id
       WHERE p.user_id = ?1 AND p.left_at IS NULL
       ORDER BY c.deadline DESC
       LIMIT 100`,
    )
    .bind(userId)
    .all<{
      id: string;
      title: string;
      mode: GameMode;
      deadline: number;
      finished_at: number | null;
      updated_at: number;
      creator: string | null;
      seen_at: number;
      players: number;
      entries: number;
      my_total: number | null;
    }>();

  const summaries = rows.results.map((r) => ({
    id: r.id,
    title: r.title,
    mode: r.mode,
    creator: r.creator ?? "",
    deadline: r.deadline,
    status: challengeStatus(r, now),
    players: r.players,
    entries: r.entries,
    myTotal: r.my_total,
    unread: r.updated_at > r.seen_at || (challengeStatus(r, now) === "finished" && r.seen_at < Math.min(r.deadline, r.finished_at ?? r.deadline)),
  }));
  const open = summaries.filter((s) => s.status === "open").sort((a, b) => a.deadline - b.deadline);
  const finished = summaries.filter((s) => s.status === "finished").slice(0, 20);
  return [...open, ...finished];
}

export interface ChallengeDetail {
  id: string;
  title: string;
  mode: GameMode;
  creator: string;
  createdAt: number;
  deadline: number;
  status: ChallengeStatus;
  /** Only while open, so any player can share it. */
  inviteToken: string | null;
  players: { nickname: string; isMe: boolean; total: number | null; finishedAt: number | null; rank: number | null }[];
}

/** Null when the user is not one of the challenge's (current) players: they may not see anything about it. */
export async function getChallengeDetail(db: D1Database, userId: string, challengeId: string, now: number): Promise<ChallengeDetail | null> {
  const challenge = await getChallengeForPlayer(db, userId, challengeId);
  if (!challenge) {
    return null;
  }
  const rows = await db
    .prepare(
      `SELECT p.user_id, u.nickname, e.total, e.finished_at
       FROM challenge_players p
       JOIN users u ON u.id = p.user_id
       LEFT JOIN challenge_entries e ON e.challenge_id = p.challenge_id AND e.user_id = p.user_id
       WHERE p.challenge_id = ?1 AND p.left_at IS NULL`,
    )
    .bind(challengeId)
    .all<{ user_id: string; nickname: string | null; total: number | null; finished_at: number | null }>();
  const creator = await db.prepare("SELECT nickname FROM users WHERE id = ?1").bind(challenge.creator_id).first<{ nickname: string | null }>();

  const sorted = [...rows.results].sort(
    (a, b) =>
      (b.total ?? -1) - (a.total ?? -1) ||
      (a.finished_at ?? Number.MAX_SAFE_INTEGER) - (b.finished_at ?? Number.MAX_SAFE_INTEGER) ||
      (a.nickname ?? "").localeCompare(b.nickname ?? ""),
  );
  let rank = 0;
  const players = sorted.map((r, i) => {
    if (r.total !== null && (i === 0 || sorted[i - 1].total !== r.total)) {
      rank = i + 1;
    }
    return {
      nickname: r.nickname ?? "",
      isMe: r.user_id === userId,
      total: r.total,
      finishedAt: r.finished_at,
      rank: r.total === null ? null : rank,
    };
  });

  const status = challengeStatus(challenge, now);
  return {
    id: challenge.id,
    title: challenge.title,
    mode: challenge.mode,
    creator: creator?.nickname ?? "",
    createdAt: challenge.created_at,
    deadline: challenge.deadline,
    status,
    inviteToken: status === "open" ? challenge.invite_token : null,
    players,
  };
}

/**
 * Links a finished game to a challenge when the player is in it, it is still open (with a short grace period for
 * games queued offline), the mode matches and they have no entry yet. Returns the events to announce: the entry
 * to the other players, and the results when this was the last player to play.
 */
export async function recordEntry(
  db: D1Database,
  player: { id: string; nickname: string },
  challengeId: string,
  game: { gameId: string; mode: GameMode; total: number },
  now: number,
): Promise<{ recorded: boolean; reason?: string; events: ChallengeEvent[] }> {
  const challenge = await getChallengeForPlayer(db, player.id, challengeId);
  if (!challenge) {
    return { recorded: false, reason: "not_a_player", events: [] };
  }
  if (challenge.finished_at !== null || now > challenge.deadline + ENTRY_GRACE_MS) {
    return { recorded: false, reason: "challenge_finished", events: [] };
  }
  if (challenge.mode !== game.mode) {
    return { recorded: false, reason: "wrong_mode", events: [] };
  }

  const inserted = await db
    .prepare(
      `INSERT INTO challenge_entries (challenge_id, user_id, client_game_id, total, finished_at) VALUES (?1, ?2, ?3, ?4, ?5)
       ON CONFLICT (challenge_id, user_id) DO NOTHING`,
    )
    .bind(challengeId, player.id, game.gameId, game.total, now)
    .run();
  if ((inserted.meta?.changes ?? 0) === 0) {
    return { recorded: false, reason: "already_played", events: [] };
  }

  const players = await activePlayers(db, challengeId);
  const entries = await db
    .prepare("SELECT user_id, total, finished_at FROM challenge_entries WHERE challenge_id = ?1")
    .bind(challengeId)
    .all<{ user_id: string; total: number; finished_at: number }>();
  const everyonePlayed = players.every((p) => entries.results.some((e) => e.user_id === p.user_id));

  await db
    .prepare("UPDATE challenges SET updated_at = ?2, finished_at = CASE WHEN ?3 = 1 THEN ?2 ELSE finished_at END WHERE id = ?1")
    .bind(challengeId, now, everyonePlayed ? 1 : 0)
    .run();
  await markSeen(db, player.id, challengeId, now);

  const others = players.map((p) => p.user_id).filter((id) => id !== player.id);
  const events: ChallengeEvent[] = [];
  if (others.length > 0) {
    events.push({ kind: "entry", challengeId, title: challenge.title, nickname: player.nickname, total: game.total, userIds: others });
  }
  if (everyonePlayed) {
    events.push(await resultsEvent(db, challenge, players.map((p) => p.user_id)));
  }
  return { recorded: true, events };
}

/**
 * Closes open challenges whose deadline has passed and returns their results events (used by the scheduled Worker).
 * Each challenge is closed once: the UPDATE only succeeds while finished_at is still empty.
 */
export async function closeExpiredChallenges(db: D1Database, now: number): Promise<ChallengeEvent[]> {
  const due = await db
    .prepare("SELECT * FROM challenges WHERE finished_at IS NULL AND deadline <= ?1 LIMIT 50")
    .bind(now)
    .all<ChallengeRow>();
  const events: ChallengeEvent[] = [];
  for (const challenge of due.results) {
    const closed = await db
      .prepare("UPDATE challenges SET finished_at = ?2, updated_at = ?2 WHERE id = ?1 AND finished_at IS NULL")
      .bind(challenge.id, now)
      .run();
    if ((closed.meta?.changes ?? 0) === 0) {
      continue;
    }
    const players = await activePlayers(db, challenge.id);
    if (players.length > 0) {
      events.push(await resultsEvent(db, challenge, players.map((p) => p.user_id)));
    }
  }
  return events;
}

/**
 * Players without an entry in open challenges whose deadline is within <paramref name="windowMs"/>, not yet reminded.
 * Challenges created with less time than the window left get no reminder (they were just announced).
 */
export async function dueReminders(
  db: D1Database,
  now: number,
  windowMs: number,
): Promise<{ challengeId: string; title: string; deadline: number; userIds: string[] }[]> {
  const rows = await db
    .prepare(
      `SELECT c.id, c.title, c.deadline, p.user_id
       FROM challenges c
       JOIN challenge_players p ON p.challenge_id = c.id AND p.left_at IS NULL AND p.reminded_at IS NULL
       LEFT JOIN challenge_entries e ON e.challenge_id = c.id AND e.user_id = p.user_id
       WHERE c.finished_at IS NULL AND c.deadline > ?1 AND c.deadline <= ?2 AND e.user_id IS NULL
         AND c.created_at <= c.deadline - ?3
       ORDER BY c.id`,
    )
    .bind(now, now + windowMs, windowMs)
    .all<{ id: string; title: string; deadline: number; user_id: string }>();

  const byChallenge = new Map<string, { challengeId: string; title: string; deadline: number; userIds: string[] }>();
  for (const r of rows.results) {
    const item = byChallenge.get(r.id) ?? { challengeId: r.id, title: r.title, deadline: r.deadline, userIds: [] };
    item.userIds.push(r.user_id);
    byChallenge.set(r.id, item);
  }
  return [...byChallenge.values()];
}

export async function markReminded(db: D1Database, challengeId: string, userIds: string[], now: number): Promise<void> {
  if (userIds.length === 0) {
    return;
  }
  await db.batch(
    userIds.map((id) =>
      db.prepare("UPDATE challenge_players SET reminded_at = ?3 WHERE challenge_id = ?1 AND user_id = ?2").bind(challengeId, id, now),
    ),
  );
}

/** Up to 20 other players whose nickname contains the text (case-insensitive), alphabetical. */
export async function searchPlayers(db: D1Database, userId: string, query: string): Promise<string[]> {
  const text = query.trim().slice(0, 16).replace(/[%_\\]/g, (c) => "\\" + c);
  const rows = await db
    .prepare(
      `SELECT nickname FROM users
       WHERE nickname IS NOT NULL AND id <> ?1 AND nickname LIKE ?2 ESCAPE '\\'
       ORDER BY nickname COLLATE NOCASE LIMIT 20`,
    )
    .bind(userId, `%${text}%`)
    .all<{ nickname: string }>();
  return rows.results.map((r) => r.nickname);
}

async function resultsEvent(db: D1Database, challenge: ChallengeRow, userIds: string[]): Promise<ChallengeEvent> {
  const winner = await db
    .prepare(
      `SELECT u.nickname, e.total FROM challenge_entries e JOIN users u ON u.id = e.user_id
       JOIN challenge_players p ON p.challenge_id = e.challenge_id AND p.user_id = e.user_id AND p.left_at IS NULL
       WHERE e.challenge_id = ?1 ORDER BY e.total DESC, e.finished_at ASC LIMIT 1`,
    )
    .bind(challenge.id)
    .first<{ nickname: string; total: number }>();
  return {
    kind: "results",
    challengeId: challenge.id,
    title: challenge.title,
    winner: winner?.nickname ?? null,
    winnerTotal: winner?.total ?? null,
    userIds,
  };
}

async function getChallengeForPlayer(db: D1Database, userId: string, challengeId: string): Promise<ChallengeRow | null> {
  if (!isChallengeId(challengeId)) {
    return null;
  }
  return await db
    .prepare(
      `SELECT c.* FROM challenges c
       JOIN challenge_players p ON p.challenge_id = c.id AND p.user_id = ?2 AND p.left_at IS NULL
       WHERE c.id = ?1`,
    )
    .bind(challengeId, userId)
    .first<ChallengeRow>();
}

async function activePlayers(db: D1Database, challengeId: string): Promise<{ user_id: string }[]> {
  const rows = await db
    .prepare("SELECT user_id FROM challenge_players WHERE challenge_id = ?1 AND left_at IS NULL")
    .bind(challengeId)
    .all<{ user_id: string }>();
  return rows.results;
}

function uniqueUsers(users: { id: string; nickname: string }[]): { id: string; nickname: string }[] {
  return [...new Map(users.map((u) => [u.id, u])).values()];
}

async function findUsersByNickname(db: D1Database, nicknames: string[]): Promise<{ id: string; nickname: string }[]> {
  if (nicknames.length === 0) {
    return [];
  }
  const placeholders = nicknames.map((_, i) => `?${i + 1}`).join(", ");
  const rows = await db
    .prepare(`SELECT id, nickname FROM users WHERE nickname IN (${placeholders})`)
    .bind(...nicknames)
    .all<{ id: string; nickname: string }>();
  return rows.results;
}
