import type { GroupError } from "./groups";
import { getSessionUser, isSafeMutation, json } from "./http";
import type { Env } from "./providers";

export interface GroupUser {
  id: string;
  nickname: string;
}

/** Signed-in player with a nickname for a group call; otherwise the error response to send. Mutations must also be safe (same-origin JSON). */
export async function groupUser(request: Request, env: Env, mutation: boolean): Promise<{ user: GroupUser } | { response: Response }> {
  if (mutation && !isSafeMutation(request)) {
    return { response: json({ error: "bad_request" }, 400) };
  }
  const user = await getSessionUser(request, env);
  if (!user) {
    return { response: json({ error: "unauthorized" }, 401) };
  }
  if (!user.nickname) {
    return { response: json({ error: "nickname_required" }, 409) };
  }
  return { user: { id: user.id, nickname: user.nickname } };
}

export function groupError(error: GroupError | string): Response {
  const status = error === "not_found" || error === "user_not_found" ? 404 : error === "forbidden" ? 403 : error === "rate_limited" || error === "too_many_requests" ? 429 : 409;
  return json({ error }, status);
}

/** Reads a JSON body with a nickname (and optional extra fields); null when it is not an object with a short nickname. */
export async function nicknameBody<T extends Record<string, unknown>>(request: Request): Promise<(T & { nickname: string }) | null> {
  const body = (await request.json().catch(() => null)) as Record<string, unknown> | null;
  return body && typeof body.nickname === "string" && body.nickname.length > 0 && body.nickname.length <= 32 ? (body as T & { nickname: string }) : null;
}