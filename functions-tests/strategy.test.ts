import { describe, expect, it } from "vitest";
import { sha256Hex } from "../functions/_lib/crypto";
import { buildStrategyPayload, STRATEGY_GAME_LIMIT } from "../functions/_lib/strategy";
import { onRequestGet } from "../functions/api/strategy";

const action = (gameId: string, seq: number, kind = "Roll") => ({
  client_game_id: gameId,
  seq,
  action: kind,
  occurred_at: 1000 + seq,
  turn: 1,
  roll_number: 1,
  dice: "12345",
  die_id: null,
  game_index: kind === "Score" ? 2 : null,
  category: kind === "Score" ? "Chance" : null,
  points: kind === "Score" ? 15 : null,
});

describe("strategy payload", () => {
  it("groups actions under their games, oldest game first and actions in order", () => {
    const payload = buildStrategyPayload(
      "Doug",
      [
        { client_game_id: "b", total: 1500, played_at: 20 },
        { client_game_id: "a", total: 1400, played_at: 10 },
      ],
      [action("b", 2, "Score"), action("a", 1), action("b", 1)],
    );
    expect(payload.limit).toBe(STRATEGY_GAME_LIMIT);
    expect(payload.games.map((g) => g.gameId)).toEqual(["a", "b"]);
    expect(payload.games[1].actions).toEqual([
      [1, "Roll", 1001, 1, 1, "12345", null, null, null, null],
      [2, "Score", 1002, 1, 1, "12345", null, 2, "Chance", 15],
    ]);
  });

  it("keeps a finished game that has no recorded actions", () => {
    const payload = buildStrategyPayload(null, [{ client_game_id: "x", total: 1200, played_at: 5 }], []);
    expect(payload.games).toEqual([{ gameId: "x", total: 1200, playedAt: 5, actions: [] }]);
  });
});

interface Call {
  sql: string;
  args: unknown[];
}

/** Minimal D1 stand-in: answers the session lookup and records every query. */
async function fakeEnv(signedIn: boolean) {
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
          return signedIn && call.args[0] === tokenHash ? { id: "user-1", nickname: "Doug" } : null;
        },
        async all() {
          if (sql.includes("FROM scores")) {
            return { results: [{ client_game_id: "g1", total: 1365, played_at: 100 }] };
          }
          return { results: [action("g1", 1)] };
        },
      };
      return statement;
    },
  };
  return { env: { DB } as never, calls };
}

const request = new Request("https://x.test/api/strategy", { headers: { Cookie: "yz_session=tok" } });

describe("GET /api/strategy", () => {
  it("requires a signed-in user", async () => {
    const { env } = await fakeEnv(false);
    const response = await onRequestGet({ request, env } as never);
    expect(response.status).toBe(401);
  });

  it("returns only the signed-in user's recent Full Sheet games", async () => {
    const { env, calls } = await fakeEnv(true);
    const response = await onRequestGet({ request, env } as never);
    expect(response.status).toBe(200);
    const body = (await response.json()) as { nickname: string; games: { gameId: string; actions: unknown[] }[] };
    expect(body.nickname).toBe("Doug");
    expect(body.games.map((g) => g.gameId)).toEqual(["g1"]);
    expect(body.games[0].actions).toHaveLength(1);

    const scores = calls.find((c) => c.sql.includes("FROM scores"))!;
    expect(scores.sql).toContain("mode = 'FullSheet'");
    expect(scores.args).toEqual(["user-1", STRATEGY_GAME_LIMIT]);
    const actions = calls.find((c) => c.sql.includes("FROM game_actions"))!;
    expect(actions.args).toEqual(["user-1", "g1"]);
  });
});
