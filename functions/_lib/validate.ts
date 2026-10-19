export type GameMode = "Single" | "FullSheet";
export type Period = "all" | "week";

// Per card: upper 105 + 35 bonus, lower 235 (30+30+25+30+40+50+30) = 375.
// Single mode may add a 100-point Yahtzee bonus up to 12 times; Full Sheet has no bonus or Joker.
const MAX_CARD = 375;
export const MAX_SCORE: Record<GameMode, number> = {
  Single: MAX_CARD + 12 * 100,
  FullSheet: MAX_CARD * 6,
};

export const MAX_SCORES_PER_HOUR = 20;
export const NICKNAME_MIN = 3;
export const NICKNAME_MAX = 16;

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const NICKNAME = /^[A-Za-z0-9][A-Za-z0-9 _-]*[A-Za-z0-9]$/;

export function parseMode(value: unknown): GameMode | null {
  return value === "Single" || value === "FullSheet" ? value : null;
}

export function parsePeriod(value: unknown): Period | null {
  return value === "all" || value === "week" ? value : null;
}

export interface ScoreSubmission {
  gameId: string;
  mode: GameMode;
  total: number;
}

export function validateScoreSubmission(body: unknown): { ok: true; value: ScoreSubmission } | { ok: false; error: string } {
  if (typeof body !== "object" || body === null) {
    return { ok: false, error: "invalid_body" };
  }
  const { gameId, mode, total } = body as Record<string, unknown>;
  if (typeof gameId !== "string" || !UUID.test(gameId)) {
    return { ok: false, error: "invalid_game_id" };
  }
  const parsedMode = parseMode(mode);
  if (!parsedMode) {
    return { ok: false, error: "invalid_mode" };
  }
  if (typeof total !== "number" || !Number.isInteger(total) || total < 0 || total > MAX_SCORE[parsedMode]) {
    return { ok: false, error: "invalid_total" };
  }
  return { ok: true, value: { gameId: gameId.toLowerCase(), mode: parsedMode, total } };
}

export function validateNickname(value: unknown): { ok: true; value: string } | { ok: false; error: string } {
  if (typeof value !== "string") {
    return { ok: false, error: "invalid_nickname" };
  }
  const nickname = value.trim().replace(/ {2,}/g, " ");
  if (nickname.length < NICKNAME_MIN || nickname.length > NICKNAME_MAX || !NICKNAME.test(nickname)) {
    return { ok: false, error: "invalid_nickname" };
  }
  return { ok: true, value: nickname };
}
