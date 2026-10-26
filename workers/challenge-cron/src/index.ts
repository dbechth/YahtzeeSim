import { closeExpiredChallenges, dueReminders, markReminded } from "../../../functions/_lib/challenges";
import { notify, pushToUsers, reminderMessage } from "../../../functions/_lib/notify";
import type { Env } from "../../../functions/_lib/providers";

/** Players who have not played get one reminder when this much time is left. */
export const REMINDER_WINDOW_MS = 6 * 60 * 60 * 1000;

/**
 * Scheduled work for challenges (cron in wrangler.toml, every 15 minutes): close challenges whose deadline has passed
 * and send their results, then remind players who still have to play. Shares its code and database with the Pages API.
 */
export async function runChallengeSchedule(env: Env, now: number, fetchFn: typeof fetch = fetch): Promise<{ closed: number; reminded: number }> {
  const results = await closeExpiredChallenges(env.DB, now);
  await notify(env, results, fetchFn);

  let reminded = 0;
  for (const due of await dueReminders(env.DB, now, REMINDER_WINDOW_MS)) {
    // Mark first, so a slow or failed push never sends the same reminder twice.
    await markReminded(env.DB, due.challengeId, due.userIds, now);
    const hoursLeft = Math.max(1, Math.round((due.deadline - now) / 3_600_000));
    await pushToUsers(env, due.userIds, reminderMessage(due.challengeId, due.title, hoursLeft), fetchFn);
    reminded += due.userIds.length;
  }

  return { closed: results.length, reminded };
}

export default {
  async scheduled(_controller: ScheduledController, env: Env, ctx: ExecutionContext): Promise<void> {
    ctx.waitUntil(runChallengeSchedule(env, Date.now()));
  },

  async fetch(): Promise<Response> {
    return new Response("YahtzeeSim challenge scheduler", { status: 200 });
  },
};
