/**
 * Badge ids the app may send. Must match YahtzeeSim.Analysis/Story/BadgeRules.cs (a .NET test checks they agree).
 */
export const STYLE_BADGES = [
  "yahtzee-hunter",
  "straight-shooter",
  "bonus-banker",
  "card-closer",
  "card-juggler",
  "sure-thing",
  "quick-draw",
  "deep-thinker",
  "all-rounder",
] as const;

export type StyleBadge = (typeof STYLE_BADGES)[number];

export interface StyleBadgeUpdate {
  badge: StyleBadge;
  version: string;
}

export function validateStyleBadge(body: unknown): { ok: true; value: StyleBadgeUpdate } | { ok: false; error: string } {
  if (typeof body !== "object" || body === null) {
    return { ok: false, error: "invalid_body" };
  }
  const { badge, version } = body as { badge?: unknown; version?: unknown };
  if (typeof badge !== "string" || !(STYLE_BADGES as readonly string[]).includes(badge)) {
    return { ok: false, error: "invalid_badge" };
  }
  if (typeof version !== "string" || !/^\d{1,4}$/.test(version)) {
    return { ok: false, error: "invalid_version" };
  }
  return { ok: true, value: { badge: badge as StyleBadge, version } };
}
