import { describe, expect, it } from "vitest";
import { MAX_SCORE, validateNickname, validateScoreSubmission } from "../functions/_lib/validate";

const gameId = "3f2b8c1e-5a4d-4b6e-9c1d-0e7f8a9b0c1d";

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
