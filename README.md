# YahtzeeSim

Yahtzee simulator with multiple front ends:

- Windows desktop app: `YahtzeeSim`
- MAUI app: `YahtzeeSim.Maui`
- Web PWA app: `YahtzeeSim.Web`

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

### One-Click Deploy After Setup

1. Open GitHub Actions.
2. Run `Deploy Yahtzee Web to Cloudflare Pages`.
3. Click Run workflow.

That is your one-click deployment flow after initial setup.

### Local Fallback Deploy (Optional)

If you prefer deploying from your machine:

```powershell
$env:CLOUDFLARE_API_TOKEN = "<token>"
$env:CLOUDFLARE_ACCOUNT_ID = "<account-id>"
$env:CLOUDFLARE_PAGES_PROJECT_NAME = "<project-name>"
./scripts/deploy-cloudflare-pages.ps1
```