# YahtzeeSim

Yahtzee simulator delivered as one web app (Blazor WebAssembly PWA):

- `YahtzeeSim.Web`: the web front end
- `YahtzeeSim.Engine`: the single game engine (no UI dependencies)
- `YahtzeeSim.Engine.Tests`: engine unit tests (`dotnet test`)
- `functions/`, `migrations/`, `wrangler.toml`: the account, leaderboard, challenges and groups API (Cloudflare Pages Functions + D1), tested with `functions-tests/` (`npm test`)
- `workers/challenge-cron`: a scheduled Worker that ends challenges and sends reminders

Players sign in with Google (no passwords are handled by this app), pick a nickname and appear on the leaderboard. See [Accounts and Leaderboard](#accounts-and-leaderboard).

## Web App Quick Start

Run this from the repository root:

```powershell
dotnet run --project .\YahtzeeSim.Web\YahtzeeSim.Web.csproj
```

Open the URL shown in the console (usually `https://localhost:xxxx`).

## Install On Your Phone (No App Store)

The app shows an install banner with these steps until it is installed.

**iPhone or iPad**

1. Open the web app URL in Safari.
2. Tap Share.
3. Tap Add to Home Screen.
4. Launch from the new home screen icon. Challenge notifications only work in this Home Screen app and need iOS 16.4 or later.

**Android**

1. Open the web app URL in Chrome.
2. Tap Install in the banner, or open the browser menu (⋮) and tap Install app or Add to Home screen.
3. Launch from the home screen or app list.

For sharing outside your local network, deploy `YahtzeeSim.Web` to a host like:

- GitHub Pages
- Cloudflare Pages
- Netlify
- Azure Static Web Apps

## Cloudflare One-Click Deployment

This repository now includes GitHub Actions deployment for Cloudflare Pages:

- Workflow file: `.github/workflows/deploy-cloudflare-pages.yml`
- Optional local deploy script: `scripts/deploy-cloudflare-pages.ps1`

### One-Time Setup (10 minutes)

1. Create a Cloudflare Pages project in the dashboard.
2. In GitHub, open repository Settings -> Secrets and variables -> Actions and add:
	- `CLOUDFLARE_API_TOKEN`
	- `CLOUDFLARE_ACCOUNT_ID`
	- `CLOUDFLARE_PAGES_PROJECT_NAME`
3. Ensure your default branch is `main` (or update the workflow trigger branch).
4. For `CLOUDFLARE_PAGES_PROJECT_NAME`, use the exact Pages project name from Cloudflare (case-sensitive).

Recommended API token permissions:

- Account -> Cloudflare Pages -> Edit
- Account Resources -> Include -> Your account

Use account-scoped token permissions (not zone-only permissions).

### One-Click Deploy After Setup

1. Open GitHub Actions.
2. Run `Deploy Yahtzee Web to Cloudflare Pages`.
3. Click Run workflow.

That is your one-click deployment flow after initial setup.

Important:

- Use your main Pages project URL (for example `your-project.pages.dev`) when sharing.
- Do not share temporary deployment preview links.
- This workflow deploys to production so your primary Pages URL stays stable.

### Troubleshooting 403 Authentication Error

If GitHub Actions fails with Cloudflare API 403 and code 10000:

1. Re-create `CLOUDFLARE_API_TOKEN` with `Cloudflare Pages:Edit` on the correct account.
2. Verify `CLOUDFLARE_ACCOUNT_ID` is the account ID (not zone ID).
3. Verify `CLOUDFLARE_PAGES_PROJECT_NAME` exactly matches your Pages project name.
4. Confirm secrets are added in the same GitHub repository that runs the workflow.

Note: Node deprecation warnings are not the root cause of this failure; the 403 is an auth/permissions mismatch.

If GitHub Actions fails with code 8000007 (Project not found):

1. `CLOUDFLARE_PAGES_PROJECT_NAME` is not an exact match for an existing Pages project in that account.
2. `CLOUDFLARE_ACCOUNT_ID` points to a different account than the one that owns the project.
3. Re-run the workflow and use the "Validate Cloudflare project exists" step output to see available project names.

### Local Fallback Deploy (Optional)

If you prefer deploying from your machine:

```powershell
$env:CLOUDFLARE_API_TOKEN = "<token>"
$env:CLOUDFLARE_ACCOUNT_ID = "<account-id>"
$env:CLOUDFLARE_PAGES_PROJECT_NAME = "<project-name>"
./scripts/deploy-cloudflare-pages.ps1
```
## Accounts and Leaderboard

Sign-in is required. The browser talks to `/api/*`, served by Cloudflare Pages Functions in `functions/` with scores stored in a D1 database. Only the Google account id and the chosen nickname are stored.

### One-Time Setup

1. **Google OAuth client** (Google Cloud Console -> APIs & Services -> Credentials -> Create OAuth client ID -> Web application):
	- Authorized redirect URIs: `https://<your-project>.pages.dev/api/auth/callback` and, for local testing, `http://localhost:8788/api/auth/callback`
	- On the consent screen set the privacy policy link to `https://<your-project>.pages.dev/privacy`
2. **D1 database**: run `npx wrangler d1 create yahtzeesim` and paste the printed `database_id` into `wrangler.toml`.
3. **Pages secrets** (Cloudflare dashboard -> your Pages project -> Settings -> Variables and Secrets, Production): add `GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET`.
4. **API token**: add `Account -> D1 -> Edit` to the existing `CLOUDFLARE_API_TOKEN` so the deploy can apply migrations (`migrations/`).
5. Push to `main` (or run the deploy workflow). Migrations are applied automatically; until step 2 is done the migration step is skipped with a warning.

### Local Development

Needs Node 22 or later.

```powershell
npm ci
copy .dev.vars.example .dev.vars      # then fill in GOOGLE_CLIENT_ID / GOOGLE_CLIENT_SECRET
dotnet publish .\YahtzeeSim.Web\YahtzeeSim.Web.csproj -c Release
npx wrangler d1 migrations apply DB --local
npx wrangler pages dev --port 8788    # open http://localhost:8788
npm test                              # API unit tests
```

`dotnet run` alone serves only the static app, so sign-in needs the `wrangler pages dev` command above.

### Challenges and Notifications

Players can challenge others to one game from the flag button in the top bar: pick a name, Single or Full Sheet, an end time (3 hours to 1 week) and players by nickname, or share the invite link (`/?join=...`). Only the players in a challenge see it. Each player's first finished challenge game counts, and it also counts on the leaderboard like any other game. A challenge ends at its end time or when everyone has played.

Players in a challenge get push notifications when they are invited, when someone posts a score, about 6 hours before the end if they have not played, and when it ends. Notifications use standard Web Push (no Firebase or other service): Android works in the browser or installed app, iPhone and iPad need the Home Screen app on iOS 16.4 or later. Each player turns them on per device in the Challenges screen.

### Groups

From Challenges, then Groups, a player can start a group (they become its admin), find a group by name and ask to join, and see their groups. A new member only gets in when a group admin approves the request; admins also see and approve requests from the group page, can add a player directly by nickname (no approval needed), can make other members admins (or plain members again) and remove members. A group always keeps at least one admin. Any member can invite a whole group they are in to a challenge, when creating it, from the invite panel, or with Challenge this group on the group page. Admins get a notification (and a count on the Groups button) when someone asks to join. The API is under `functions/api/groups/` (rules in `functions/_lib/groups.ts`, tables in `migrations/0006_groups.sql`).

The end-of-challenge results and reminders come from a small scheduled Worker (`workers/challenge-cron`, every 15 minutes) that the deploy workflow publishes after the site.

One-time setup:

1. **API token**: add `Account -> Workers Scripts -> Edit` to `CLOUDFLARE_API_TOKEN` so the workflow can deploy the Worker. Until then the deploy shows a warning; challenges still end on time in the app, but nobody gets the results or reminder notifications.
2. **Push keys**: run this once (needs `npm ci` and `npx wrangler login`). It creates the keys and stores them as secrets on the Pages project and the Worker. Keep them: new keys mean every player has to turn notifications on again, so re-running needs `-Force`.

	```powershell
	.\scripts\setup-push.ps1 -Subject mailto:you@example.com
	```

3. Redeploy (run the workflow) so the site picks up the secrets. Until the keys exist the app simply hides the notification option.

Locally, `.\scripts\setup-push.ps1 -Subject mailto:you@example.com -Local` writes the keys to `.dev.vars` for `wrangler pages dev`. To run the scheduler locally: `npx wrangler dev --config workers/challenge-cron/wrangler.toml --test-scheduled`, then open `http://localhost:8787/__scheduled`.

### Exporting Game Data

Download users, scores and every recorded action to `exports/` (JSON and CSV, git-ignored because it is personal data):

```powershell
npm ci
npx wrangler login                    # or set CLOUDFLARE_API_TOKEN / CLOUDFLARE_ACCOUNT_ID
.\scripts\export-game-data.ps1        # production database
.\scripts\export-game-data.ps1 -Local # local `wrangler pages dev` database
```

Use `-OutputDir <path>` to write elsewhere.

### Analysing Full Sheet Strategy

After exporting, build a rule-based strategy report from the finished Full Sheet games:

```powershell
.\scripts\analyze-strategy.ps1 -Open             # writes exports/analysis/ and opens index.html
.\scripts\analyze-strategy.ps1 -Simulations 5000 # more simulated reference games (slower, tighter percentiles)
```

Each game is replayed from its actions and must reproduce the submitted total. Every keep, early stop and box choice is graded against a reference strategy built on an exact single-card solver (245.87 points per card), and each total is split into baseline + Full Sheet choice + dice luck − decision cost. Findings come only from the fixed rule table in `YahtzeeSim.Analysis/Rules/StrategyRules.cs`; each shows its condition, threshold and evidence. The output (`index.html`, one HTML and Markdown page per player, a `<player>-story.html` play-style story, `decisions.csv`) is personal data and stays in the git-ignored `exports/` folder. The same export and seed always give the same report.

Players see a short **play-style story** for their own last 10 finished Full Sheet games in the app under **Account → My play style**: their badge, what they go for with each kind of opening roll (with dice examples), how they fill the sheet, highlights and one tip. Their badge is also shown on the leaderboard (`PUT /api/style`, migration `0003`). The full report is only in the script and the research app. The app fetches the games from `GET /api/strategy` and runs the analysis in the browser, using the precomputed solver table and reference simulations in `YahtzeeSim.Web/wwwroot/analysis/` (the solver is only needed for the tip). If the scoring rules, the reference strategy or the simulation change, regenerate those files (the analysis tests fail until you do):

```powershell
dotnet run --project YahtzeeSim.Analysis.Cli -c Release -- --write-app-assets YahtzeeSim.Web/wwwroot/analysis
```

### Research Desktop App (Windows)

`YahtzeeSim.Research` is a WPF app for the owner's research. It needs the WebView2 runtime (built into Windows 11) and, to pull data, the same Node/wrangler setup as the export script.

```powershell
.\scripts\install-research.ps1   # once: builds it and adds "YahtzeeSim Research" to the Start menu and desktop
.\scripts\research.ps1           # or run it straight from source
```

Run `install-research.ps1` again after pulling code changes to update the installed app (`-NoDesktop` skips the desktop shortcut, `-Uninstall` removes the shortcuts). The app is published to `YahtzeeSim.Research\bin\publish` inside the repository so it can find `exports/` and the export script.

- **Pull latest data** runs `scripts/export-game-data.ps1`, then re-analyses. **Re-run analysis** uses the current export.
- Each player has four views: **Overview** (all players, method, rules), **Full report**, **Story preview** (the short play-style story with badge and tip, exactly as the player will see it) and **Badge metrics** (each badge's measurement against its threshold and the reference strategy, for tuning).
- It uses the solver table and reference simulations shipped with the web app, and also saves all reports to `exports/analysis/`.

The story and badge rules are versioned in `YahtzeeSim.Analysis/Story/StoryRules.cs` and `BadgeRules.cs`. Badges describe play style only, never skill; a player gets the qualifying badge with the greatest strength, or All-Rounder.

### Adding Facebook Later

Add a provider entry in `functions/_lib/providers.ts` (endpoints, scopes, claim checks), add its client id and secret as Pages secrets, and add a button in `YahtzeeSim.Web/Components/SignInGate.razor`.