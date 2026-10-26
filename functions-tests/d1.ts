import { DatabaseSync } from "node:sqlite";
import { sha256Hex } from "../functions/_lib/crypto";
import type { Env } from "../functions/_lib/providers";

const migrations = import.meta.glob("../migrations/*.sql", { query: "?raw", import: "default", eager: true });

/** An in-memory D1 database with every migration applied, for API tests that need real SQL. */
export function createTestDb(): D1Database {
  const db = new DatabaseSync(":memory:");
  db.exec("PRAGMA foreign_keys = ON;");
  for (const name of Object.keys(migrations).sort()) {
    db.exec(migrations[name]);
  }

  const prepare = (sql: string) => {
    let params: unknown[] = [];
    const statement = {
      bind(...values: unknown[]) {
        params = values.map((v) => (v === undefined ? null : v));
        return statement;
      },
      async first<T>(): Promise<T | null> {
        return (db.prepare(sql).get(...params) as T | undefined) ?? null;
      },
      async all<T>(): Promise<{ results: T[]; success: true }> {
        return { results: db.prepare(sql).all(...params) as T[], success: true };
      },
      async run() {
        const result = db.prepare(sql).run(...params);
        return { success: true, meta: { changes: Number(result.changes) } };
      },
    };
    return statement;
  };

  return {
    prepare,
    async batch(statements: { run(): Promise<unknown> }[]) {
      db.exec("BEGIN");
      try {
        const results = [];
        for (const s of statements) {
          results.push(await s.run());
        }
        db.exec("COMMIT");
        return results;
      } catch (error) {
        db.exec("ROLLBACK");
        throw error;
      }
    },
    async exec(sql: string) {
      db.exec(sql);
      return { count: 0, duration: 0 };
    },
  } as unknown as D1Database;
}

export interface TestUser {
  id: string;
  nickname: string;
  token: string;
}

/** Adds a user with a nickname and a session; returns what a request needs to act as them. */
export async function addUser(db: D1Database, nickname: string): Promise<TestUser> {
  const id = crypto.randomUUID();
  const token = "tok-" + nickname.toLowerCase().replace(/\W/g, "");
  await db.prepare("INSERT INTO users (id, provider, provider_sub, nickname, created_at) VALUES (?1, 'google', ?2, ?3, 0)").bind(id, id, nickname).run();
  await db.prepare("INSERT INTO sessions (token_hash, user_id, expires_at) VALUES (?1, ?2, ?3)").bind(await sha256Hex(token), id, Date.now() + 3_600_000).run();
  return { id, nickname, token };
}

export function testEnv(db: D1Database, extra: Partial<Env> = {}): Env {
  return { DB: db, ...extra } as Env;
}

/** A same-origin request as a signed-in user. */
export function request(user: TestUser | null, method: string, path: string, body?: unknown): Request {
  const headers: Record<string, string> = { Origin: "https://x.test" };
  if (user) {
    headers.Cookie = `yz_session=${user.token}`;
  }
  if (body !== undefined) {
    headers["Content-Type"] = "application/json";
  }
  return new Request("https://x.test" + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
}

/** Calls a Pages Function handler with collected waitUntil promises (awaited, so notifications finish in tests). */
export async function call(
  handler: (context: never) => Response | Promise<Response>,
  env: Env,
  req: Request,
  params: Record<string, string> = {},
): Promise<{ status: number; body: Record<string, unknown> }> {
  const pending: Promise<unknown>[] = [];
  const response = await handler({ request: req, env, params, waitUntil: (p: Promise<unknown>) => pending.push(p) } as never);
  await Promise.all(pending);
  const text = await response.text();
  return { status: response.status, body: text ? JSON.parse(text) : {} };
}
