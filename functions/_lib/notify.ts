import type { ChallengeEvent } from "./challenges";
import type { Env } from "./providers";
import { sendPush, type VapidKeys } from "./webpush";

/** What a notification says. Fixed templates, no punctuation, like the rest of the app's text. */
export interface PushMessage {
  title: string;
  body: string;
  /** Opened when the notification is tapped. */
  url: string;
  /** Replaces an earlier notification for the same challenge instead of stacking. */
  tag: string;
}

export function messageFor(event: ChallengeEvent): PushMessage {
  const url = `/?challenge=${encodeURIComponent(event.challengeId)}`;
  const tag = `challenge-${event.challengeId}`;
  switch (event.kind) {
    case "invited":
      return { title: `${event.byNickname} challenged you`, body: `${event.title} Tap to play your game`, url, tag };
    case "entry":
      return { title: event.title, body: `${event.nickname} scored ${event.total} Can you beat it`, url, tag };
    case "results":
      return {
        title: `${event.title} is over`,
        body: event.winner ? `${event.winner} won with ${event.winnerTotal} Tap to see the results` : "Nobody played this one Tap to see it",
        url,
        tag,
      };
  }
}

export function reminderMessage(challengeId: string, title: string, hoursLeft: number): PushMessage {
  const left = hoursLeft <= 1 ? "less than an hour" : `about ${hoursLeft} hours`;
  return {
    title,
    body: `You have ${left} left to play your game`,
    url: `/?challenge=${encodeURIComponent(challengeId)}`,
    tag: `challenge-${challengeId}`,
  };
}

export function vapidKeys(env: Pick<Env, "VAPID_PUBLIC_KEY" | "VAPID_PRIVATE_KEY" | "VAPID_SUBJECT">): VapidKeys | null {
  return env.VAPID_PUBLIC_KEY && env.VAPID_PRIVATE_KEY
    ? { publicKey: env.VAPID_PUBLIC_KEY, privateKey: env.VAPID_PRIVATE_KEY, subject: env.VAPID_SUBJECT || "mailto:admin@example.com" }
    : null;
}

/** Sends each event to every device of its players. Quietly does nothing until the VAPID secrets are configured. */
export async function notify(env: Env, events: ChallengeEvent[], fetchFn: typeof fetch = fetch): Promise<number> {
  let sent = 0;
  for (const event of events) {
    sent += await pushToUsers(env, event.userIds, messageFor(event), fetchFn);
  }
  return sent;
}

export async function pushToUsers(env: Env, userIds: string[], message: PushMessage, fetchFn: typeof fetch = fetch): Promise<number> {
  const keys = vapidKeys(env);
  if (!keys || userIds.length === 0) {
    return 0;
  }

  const placeholders = userIds.map((_, i) => `?${i + 1}`).join(", ");
  const subscriptions = await env.DB.prepare(`SELECT endpoint, p256dh, auth FROM push_subscriptions WHERE user_id IN (${placeholders})`)
    .bind(...userIds)
    .all<{ endpoint: string; p256dh: string; auth: string }>();

  let sent = 0;
  const now = Date.now();
  for (const subscription of subscriptions.results) {
    const outcome = await sendPush(subscription, message, keys, { topic: message.tag.slice(0, 32) }, fetchFn);
    if (outcome === "gone") {
      await env.DB.prepare("DELETE FROM push_subscriptions WHERE endpoint = ?1").bind(subscription.endpoint).run();
    } else if (outcome === "sent") {
      sent++;
      await env.DB.prepare("UPDATE push_subscriptions SET last_success_at = ?2 WHERE endpoint = ?1").bind(subscription.endpoint, now).run();
    }
  }

  return sent;
}
