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

export const ACTION_TYPES = ["Roll", "Hold", "Unhold", "Score", "Scratch", "Undo"] as const;
export type ActionType = (typeof ACTION_TYPES)[number];

export const SCORE_CATEGORIES = [
  "Aces",
  "Twos",
  "Threes",
  "Fours",
  "Fives",
  "Sixes",
  "ThreeOfAKind",
  "FourOfAKind",
  "FullHouse",
  "SmallStraight",
  "LargeStraight",
  "Yahtzee",
  "Chance",
] as const;

export const MAX_ACTIONS_PER_BATCH = 100;
export const MAX_ACTIONS_PER_GAME = 5000;
export const MAX_ACTIONS_PER_HOUR = 10_000;

const MIN_ACTION_TIME = Date.UTC(2020, 0, 1);
const CLOCK_SKEW_MS = 86_400_000;
const MAX_TURNS: Record<GameMode, number> = { Single: 13, FullSheet: 78 };
const DICE = /^[0-6]{5}$/;

export interface GameAction {
  seq: number;
  type: ActionType;
  at: number;
  turn: number;
  roll: number;
  dice: string;
  dieId: number | null;
  gameIndex: number | null;
  category: string | null;
  points: number | null;
  bonus: 0 | 1;
}

export interface ActionBatch {
  gameId: string;
  mode: GameMode;
  actions: GameAction[];
}

function isInt(value: unknown, min: number, max: number): value is number {
  return typeof value === "number" && Number.isInteger(value) && value >= min && value <= max;
}

function validateAction(raw: unknown, mode: GameMode, now: number): GameAction | null {
  if (typeof raw !== "object" || raw === null) {
    return null;
  }
  const a = raw as Record<string, unknown>;
  const type = ACTION_TYPES.find((t) => t === a.type);
  if (
    !type ||
    !isInt(a.seq, 1, MAX_ACTIONS_PER_GAME) ||
    !isInt(a.at, MIN_ACTION_TIME, now + CLOCK_SKEW_MS) ||
    !isInt(a.turn, 1, MAX_TURNS[mode]) ||
    !isInt(a.roll, 0, 3) ||
    typeof a.dice !== "string" ||
    !DICE.test(a.dice)
  ) {
    return null;
  }

  // No roll yet means blank dice; once rolled every die shows 1 to 6.
  if ((a.roll === 0) !== (a.dice === "00000") || (a.roll > 0 && a.dice.includes("0"))) {
    return null;
  }

  const dieId = a.dieId ?? null;
  const gameIndex = a.gameIndex ?? null;
  const category = a.category ?? null;
  const points = a.points ?? null;
  const bonus = a.bonus ?? false;

  const isScore = type === "Score" || type === "Scratch";
  const isHold = type === "Hold" || type === "Unhold";
  if (isHold ? !isInt(dieId, 1, 5) : dieId !== null) {
    return null;
  }
  if (isScore) {
    const maxCard = mode === "Single" ? 0 : 5;
    const pointsOk = type === "Scratch" ? points === 0 : isInt(points, 0, 50);
    if (!isInt(gameIndex, 0, maxCard) || !SCORE_CATEGORIES.some((c) => c === category) || !pointsOk) {
      return null;
    }
  } else if (gameIndex !== null || category !== null || points !== null || bonus !== false) {
    return null;
  }
  if (type !== "Undo" && a.roll < 1) {
    return null;
  }
  if (typeof bonus !== "boolean") {
    return null;
  }

  return {
    seq: a.seq,
    type,
    at: a.at,
    turn: a.turn,
    roll: a.roll,
    dice: a.dice,
    dieId: dieId as number | null,
    gameIndex: gameIndex as number | null,
    category: category as string | null,
    points: points as number | null,
    bonus: bonus ? 1 : 0,
  };
}

/** Checks one upload and returns normalized actions; anything unexpected rejects the whole batch. */
export function validateActionBatch(body: unknown, now = Date.now()): { ok: true; value: ActionBatch } | { ok: false; error: string } {
  if (typeof body !== "object" || body === null) {
    return { ok: false, error: "invalid_body" };
  }
  const { gameId, mode, actions } = body as Record<string, unknown>;
  if (typeof gameId !== "string" || !UUID.test(gameId)) {
    return { ok: false, error: "invalid_game_id" };
  }
  const parsedMode = parseMode(mode);
  if (!parsedMode) {
    return { ok: false, error: "invalid_mode" };
  }
  if (!Array.isArray(actions) || actions.length < 1 || actions.length > MAX_ACTIONS_PER_BATCH) {
    return { ok: false, error: "invalid_actions" };
  }
  const parsed: GameAction[] = [];
  for (const raw of actions) {
    const action = validateAction(raw, parsedMode, now);
    if (!action) {
      return { ok: false, error: "invalid_action" };
    }
    parsed.push(action);
  }
  return { ok: true, value: { gameId: gameId.toLowerCase(), mode: parsedMode, actions: parsed } };
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
