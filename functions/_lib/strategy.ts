/** The strategy report reads a player's own recent finished Full Sheet games, with every recorded action. */
export const STRATEGY_GAME_LIMIT = 10;

export interface StrategyGameRow {
  client_game_id: string;
  total: number;
  played_at: number;
}

export interface StrategyActionRow {
  client_game_id: string;
  seq: number;
  action: string;
  occurred_at: number;
  turn: number;
  roll_number: number;
  dice: string;
  die_id: number | null;
  game_index: number | null;
  category: string | null;
  points: number | null;
}

/** [seq, action, occurredAt, turn, rollNumber, dice, dieId, gameIndex, category, points] keeps the payload small. */
export type CompactAction = [number, string, number, number, number, string, number | null, number | null, string | null, number | null];

export interface StrategyGame {
  gameId: string;
  total: number;
  playedAt: number;
  actions: CompactAction[];
}

export interface StrategyPayload {
  nickname: string | null;
  limit: number;
  games: StrategyGame[];
}

/** Groups action rows under their games, oldest game first, actions in sequence order. */
export function buildStrategyPayload(nickname: string | null, games: StrategyGameRow[], actions: StrategyActionRow[]): StrategyPayload {
  const byGame = new Map<string, CompactAction[]>();
  for (const a of [...actions].sort((x, y) => x.seq - y.seq)) {
    let list = byGame.get(a.client_game_id);
    if (!list) {
      list = [];
      byGame.set(a.client_game_id, list);
    }
    list.push([a.seq, a.action, a.occurred_at, a.turn, a.roll_number, a.dice, a.die_id, a.game_index, a.category, a.points]);
  }

  return {
    nickname,
    limit: STRATEGY_GAME_LIMIT,
    games: [...games]
      .sort((x, y) => x.played_at - y.played_at || x.client_game_id.localeCompare(y.client_game_id))
      .map((g) => ({ gameId: g.client_game_id, total: g.total, playedAt: g.played_at, actions: byGame.get(g.client_game_id) ?? [] })),
  };
}
