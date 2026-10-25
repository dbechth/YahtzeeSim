import { describe, expect, it } from "vitest";
import { STYLE_BADGES, validateStyleBadge } from "../functions/_lib/badges";
import { sha256Hex } from "../functions/_lib/crypto";
import { onRequestGet as getLeaderboard } from "../functions/api/leaderboard";
import { onRequestPut } from "../functions/api/style";

describe("style badge validation", () => {
  it("accepts every known badge with a numeric rules version", () => {
    for (const badge of STYLE_BADGES) {
      expect(validateStyleBadge({ badge, version: "1" })).toEqual({ ok: true, value: { badge, version: "1" } });
    }
  });

  it.each([
    ["no body", null, "invalid_body"],
    ["unknown badge", { badge: "best-player", version: "1" }, "invalid_badge"],
    ["badge not a string", { badge: 3, version: "1" }, "invalid_badge"],
    ["missing version", { badge: "quick-draw" }, "invalid_version"],
    ["odd version", { badge: "quick-draw", version: "1.0" }, "invalid_version"],
  ])("rejects %s", (_name, body, error) => {
    expect(validateStyleBadge(body)).toEqual({ ok: false, error });
  });
});

interface Call {
  sql: string;
  args: unknown[];
}

/** Minimal D1 stand-in: answers the session lookup and records every query. */
async function fakeEnv(answers: { all?: unknown[]; first?: unknown } = {}) {
  const calls: Call[] = [];
  const tokenHash = await sha256Hex("tok");
  const DB = {
    prepare(sql: string) {
      const call: Call = { sql, args: [] };
      calls.push(call);
      const statement = {
        bind(...args: unknown[]) {
          call.args = args;
          return statement;
        },
        async first() {
          if (sql.includes("FROM sessions")) {
            return call.args[0] === tokenHash ? { id: "user-1", nickname: "Doug" } : null;
          }
          return sql.includes("COUNT(*) AS n") ? { n: 0 } : (answers.first ?? null);
        },
        async all() {
          return { results: answers.all ?? [] };
        },
        async run() {
          return { success: true };
        },
      };
      return statement;
    },
  };
  return { env: { DB } as never, calls };
}

const put = (body: unknown, cookie = "yz_session=tok") =>
  new Request("https://x.test/api/style", {
    method: "PUT",
    headers: { Cookie: cookie, "Content-Type": "application/json", Origin: "https://x.test" },
    body: JSON.stringify(body),
  });

describe("PUT /api/style", () => {
  it("requires a signed-in user", async () => {
    const { env } = await fakeEnv();
    const response = await onRequestPut({ request: put({ badge: "quick-draw", version: "1" }, "yz_session=nope"), env } as never);
    expect(response.status).toBe(401);
  });

  it("rejects unknown badges", async () => {
    const { env, calls } = await fakeEnv();
    const response = await onRequestPut({ request: put({ badge: "champion", version: "1" }), env } as never);
    expect(response.status).toBe(400);
    expect(calls.some((c) => c.sql.startsWith("UPDATE"))).toBe(false);
  });

  it("stores the badge for the signed-in user only", async () => {
    const { env, calls } = await fakeEnv();
    const response = await onRequestPut({ request: put({ badge: "deep-thinker", version: "1" }), env } as never);
    expect(response.status).toBe(200);
    const update = calls.find((c) => c.sql.startsWith("UPDATE users"))!;
    expect(update.args).toEqual(["deep-thinker", "1", "user-1"]);
  });

  it("refuses cross-site requests", async () => {
    const { env } = await fakeEnv();
    const request = new Request("https://x.test/api/style", {
      method: "PUT",
      headers: { Cookie: "yz_session=tok", "Content-Type": "application/json", Origin: "https://evil.test" },
      body: JSON.stringify({ badge: "quick-draw", version: "1" }),
    });
    expect((await onRequestPut({ request, env } as never)).status).toBe(400);
  });
});

describe("GET /api/leaderboard badges", () => {
  it("returns each player's badge and your own", async () => {
    const { env, calls } = await fakeEnv({
      all: [
        { nickname: "Gaye", badge: "yahtzee-hunter", best: 1725, low: 1723, avg: 1724, games: 2 },
        { nickname: "Doug", badge: null, best: 1365, low: 1365, avg: 1365, games: 1 },
      ],
      first: { best: 1365, low: 1365, avg: 1365, games: 1, badge: "deep-thinker" },
    });
    const request = new Request("https://x.test/api/leaderboard?mode=FullSheet&period=all", { headers: { Cookie: "yz_session=tok" } });
    const body = (await (await getLeaderboard({ request, env } as never)).json()) as {
      entries: { nickname: string; badge: string | null; rank: number }[];
      me: { badge: string | null };
    };
    expect(body.entries.map((e) => [e.nickname, e.badge, e.rank])).toEqual([
      ["Gaye", "yahtzee-hunter", 1],
      ["Doug", null, 2],
    ]);
    expect(body.me.badge).toBe("deep-thinker");
    expect(calls.find((c) => c.sql.includes("GROUP BY s.user_id"))!.sql).toContain("u.style_badge AS badge");
  });
});
