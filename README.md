# YahtzeeSim

Yahtzee simulator delivered as one web app (Blazor WebAssembly PWA):

- `YahtzeeSim.Web`: the web front end
- `YahtzeeSim.Engine`: the single game engine (no UI dependencies)
- `YahtzeeSim.Engine.Tests`: engine unit tests (`dotnet test`)
- `functions/`, `migrations/`, `wrangler.toml`: the account and leaderboard API (Cloudflare Pages Functions + D1), tested with `functions-tests/` (`npm test`)

Players sign in with Google (no passwords are handled by this app), pick a nickname and appear on the leaderboard. See [Accounts and Leaderboard](#accounts-and-leaderboard).

## Web App Quick Start

Run this from the repository root:

```powershell
dotnet run --project .\YahtzeeSim.Web\YahtzeeSim.Web.csproj
```

Open the URL shown in the console (usually `https://localhost:xxxx`).

## Install On iPhone/iPad (No App Store)

1. Open the web app URL in Safari.
2. Tap Share.
3. Tap Add to Home Screen.
4. Launch from the new home screen icon.

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

### Exporting Game Data

Download users, scores and every recorded action to `exports/` (JSON and CSV, git-ignored because it is personal data):

```powershell
npm ci
npx wrangler login                    # or set CLOUDFLARE_API_TOKEN / CLOUDFLARE_ACCOUNT_ID
.\scripts\export-game-data.ps1        # production database
.\scripts\export-game-data.ps1 -Local # local `wrangler pages dev` database
```

Use `-OutputDir <path>` to write elsewhere.

### Adding Facebook Later

Add a provider entry in `functions/_lib/providers.ts` (endpoints, scopes, claim checks), add its client id and secret as Pages secrets, and add a button in `YahtzeeSim.Web/Components/SignInGate.razor`.