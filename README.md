# YahtzeeSim

Yahtzee simulator delivered as one web app (Blazor WebAssembly PWA):

- `YahtzeeSim.Web`: the web front end
- `YahtzeeSim.Engine`: the single game engine (no UI dependencies)
- `YahtzeeSim.Engine.Tests`: engine unit tests (`dotnet test`)

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