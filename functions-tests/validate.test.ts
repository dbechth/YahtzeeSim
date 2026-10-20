import { describe, expect, it } from "vitest";
import {
  MAX_ACTIONS_PER_BATCH,
  MAX_SCORE,
  validateActionBatch,
  validateNickname,
  validateScoreSubmission,
} from "../functions/_lib/validate";

const gameId = "3f2b8c1e-5a4d-4b6e-9c1d-0e7f8a9b0c1d";

describe("validateActionBatch", () => {
  const now = Date.UTC(2026, 9, 20);
  const base = { turn: 1, at: now - 1000 };
  const roll = { ...base, seq: 1, type: "Roll", roll: 1, dice: "35516" };
  const hold = { ...base, seq: 2, type: "Hold", roll: 1, dice: "35516", dieId: 2 };
  const score = { ...base, seq: 3, type: "Score", roll: 1, dice: "35516", category: "Chance", gameIndex: 0, points: 20, bonus: false };
  const batch = (actions: unknown[], mode = "Single") => validateActionBatch({ gameId, mode, actions }, now);

  it("accepts rolls, holds and scores and normalizes optional fields", () => {
    const result = validateActionBatch({ gameId: gameId.toUpperCase(), mode: "Single", actions: [roll, hold, score] }, now);
    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.value.gameId).toBe(gameId);
      expect(result.value.actions[0]).toMatchObject({ type: "Roll", dieId: null, category: null, bonus: 0 });
      expect(result.value.actions[1]).toMatchObject({ type: "Hold", dieId: 2 });
      expect(result.value.actions[2]).toMatchObject({ type: "Score", category: "Chance", points: 20, bonus: 0 });
    }
  });

  it("accepts a scratch worth zero, an undo back to blank dice and a Yahtzee bonus", () => {
    expect(batch([{ ...score, type: "Scratch", points: 0 }]).ok).toBe(true);
    expect(batch([{ ...base, seq: 4, type: "Undo", roll: 0, dice: "00000" }]).ok).toBe(true);
    expect(batch([{ ...score, bonus: true }]).ok).toBe(true);
  });

  it("allows other cards only in Full Sheet mode and more turns than Single", () => {
    expect(batch([{ ...score, gameIndex: 3 }]).ok).toBe(false);
    expect(batch([{ ...score, gameIndex: 3, turn: 40 }], "FullSheet").ok).toBe(true);
    expect(batch([{ ...score, turn: 14 }]).ok).toBe(false);
  });

  it.each([
    ["unknown type", { ...roll, type: "Shake" }],
    ["fractional seq", { ...roll, seq: 1.5 }],
    ["seq zero", { ...roll, seq: 0 }],
    ["time in the far future", { ...roll, at: now + 10 * 86_400_000 }],
    ["time before the app existed", { ...roll, at: 1000 }],
    ["roll number 4", { ...roll, roll: 4 }],
    ["blank dice after a roll", { ...roll, dice: "35506" }],
    ["rolled dice with roll number 0", { ...roll, roll: 0 }],
    ["die value 7", { ...roll, dice: "35517" }],
    ["short dice", { ...roll, dice: "3551" }],
    ["hold without a die id", { ...hold, dieId: undefined }],
    ["hold of die 6", { ...hold, dieId: 6 }],
    ["roll with a die id", { ...roll, dieId: 1 }],
    ["roll with a category", { ...roll, category: "Chance" }],
    ["score with an unknown category", { ...score, category: "Bingo" }],
    ["score without a card", { ...score, gameIndex: undefined }],
    ["score over 50", { ...score, points: 51 }],
    ["scratch with points", { ...score, type: "Scratch", points: 5 }],
    ["score before any roll", { ...score, roll: 0, dice: "00000" }],
    ["bonus that is not a boolean", { ...score, bonus: 1 }],
    ["bonus on a roll", { ...roll, bonus: true }],
    ["non-object", "roll"],
  ])("rejects %s", (_name, action) => {
    expect(batch([action]).ok).toBe(false);
  });

  it("rejects the whole batch when one action is bad", () => {
    expect(batch([roll, { ...hold, dieId: 9 }]).ok).toBe(false);
  });

  it.each([
    [null],
    [{ gameId: "nope", mode: "Single", actions: [roll] }],
    [{ gameId, mode: "Triple", actions: [roll] }],
    [{ gameId, mode: "Single", actions: [] }],
    [{ gameId, mode: "Single", actions: "roll" }],
    [{ gameId, mode: "Single" }],
  ])("rejects batch %j", (body) => {
    expect(validateActionBatch(body, now).ok).toBe(false);
  });

  it("limits the batch size", () => {
    const many = Array.from({ length: MAX_ACTIONS_PER_BATCH + 1 }, (_, i) => ({ ...roll, seq: i + 1 }));
    expect(batch(many.slice(0, MAX_ACTIONS_PER_BATCH)).ok).toBe(true);
    expect(batch(many).ok).toBe(false);
  });
});

describe("validateScoreSubmission", () => {
  it("accepts a valid submission and lower-cases the game id", () => {
    const result = validateScoreSubmission({ gameId: gameId.toUpperCase(), mode: "Single", total: 250 });
    expect(result).toEqual({ ok: true, value: { gameId, mode: "Single", total: 250 } });
  });

  it("accepts the exact maximum for each mode", () => {
    expect(validateScoreSubmission({ gameId, mode: "Single", total: MAX_SCORE.Single }).ok).toBe(true);
    expect(validateScoreSubmission({ gameId, mode: "FullSheet", total: MAX_SCORE.FullSheet }).ok).toBe(true);
  });

  it("uses the Yahtzee-bonus ceiling only for Single mode", () => {
    expect(MAX_SCORE.Single).toBe(1575);
    expect(MAX_SCORE.FullSheet).toBe(2250);
    expect(validateScoreSubmission({ gameId, mode: "Single", total: 1576 }).ok).toBe(false);
    expect(validateScoreSubmission({ gameId, mode: "FullSheet", total: 2251 }).ok).toBe(false);
  });

  it.each([
    [null],
    ["text"],
    [{ gameId: "nope", mode: "Single", total: 10 }],
    [{ gameId, mode: "Triple", total: 10 }],
    [{ gameId, mode: "Single", total: -1 }],
    [{ gameId, mode: "Single", total: 10.5 }],
    [{ gameId, mode: "Single", total: "10" }],
    [{ gameId, mode: "Single" }],
  ])("rejects %j", (body) => {
    expect(validateScoreSubmission(body).ok).toBe(false);
  });
});

describe("validateNickname", () => {
  it.each(["Ace", "dice_master", "Lucky 7", "a-b"])("accepts %s", (name) => {
    expect(validateNickname(name)).toEqual({ ok: true, value: name });
  });

  it("trims and collapses repeated spaces", () => {
    expect(validateNickname("  Big   Roller ")).toEqual({ ok: true, value: "Big Roller" });
  });

  it.each(["ab", "x".repeat(17), "_hidden", "ends-", "emoji🎲", "<script>", "a@b.com", "", "  ", 5, null])("rejects %j", (name) => {
    expect(validateNickname(name).ok).toBe(false);
  });

  it("allows exactly 16 characters", () => {
    expect(validateNickname("x".repeat(16)).ok).toBe(true);
  });
});
