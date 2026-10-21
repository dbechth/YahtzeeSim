export interface BoardRow {
  nickname: string;
  /** High score in the period; entries are ranked by this. */
  best: number;
  low: number;
  avg: number;
  games: number;
}

export interface RankedRow extends BoardRow {
  rank: number;
}

/** Start of the current week (Monday 00:00 UTC) in epoch milliseconds. */
export function weekStart(nowMs: number): number {
  const date = new Date(nowMs);
  const daysSinceMonday = (date.getUTCDay() + 6) % 7;
  return Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate() - daysSinceMonday);
}

/** Competition ranking: equal scores share a rank and the next rank skips (1, 2, 2, 4). */
export function rankRows(rows: BoardRow[]): RankedRow[] {
  let rank = 0;
  return rows.map((row, index) => {
    if (index === 0 || rows[index - 1].best !== row.best) {
      rank = index + 1;
    }
    return { ...row, rank };
  });
}