<#
.SYNOPSIS
    One-time setup for challenge notifications: creates the Web Push (VAPID) keys and stores them as Cloudflare secrets.

.DESCRIPTION
    Generates a P-256 key pair and sets VAPID_PUBLIC_KEY, VAPID_PRIVATE_KEY and VAPID_SUBJECT on:
      - the Pages project (the API sends invite, score and result notifications), and
      - the yahtzeesim-challenges Worker (deadline results and reminders); it is deployed first if needed.
    Needs `npm ci` and `npx wrangler login` (or CLOUDFLARE_API_TOKEN / CLOUDFLARE_ACCOUNT_ID with Pages, D1 and
    Workers Scripts edit rights).

    Keep the same keys afterwards: browsers subscribe with the public key, so changing it means every player has to
    turn notifications on again. Re-running this script without -Force refuses for that reason.

    -Local writes the keys to .dev.vars (git-ignored) instead, for `wrangler pages dev`.

.PARAMETER Subject
    Contact for the push services, e.g. mailto:you@example.com.

.PARAMETER ProjectName
    Cloudflare Pages project name. Defaults to $env:CLOUDFLARE_PAGES_PROJECT_NAME, then "yahtzeesim".

.EXAMPLE
    .\scripts\setup-push.ps1 -Subject mailto:you@example.com
    .\scripts\setup-push.ps1 -Subject mailto:you@example.com -Local
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^(mailto:|https://)')]
    [string]$Subject,
    [string]$ProjectName = "",
    [switch]$Local,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    if ([string]::IsNullOrWhiteSpace($ProjectName)) {
        $ProjectName = if ($env:CLOUDFLARE_PAGES_PROJECT_NAME) { $env:CLOUDFLARE_PAGES_PROJECT_NAME } else { "yahtzeesim" }
    }

    $marker = Join-Path $repoRoot ".vapid-configured"
    if (-not $Local -and (Test-Path $marker) -and -not $Force) {
        throw "Push keys were already set up on $(Get-Content $marker). Changing them makes every player turn notifications on again. Use -Force if you really mean it."
    }

    # Node's WebCrypto creates the key pair: public key as the raw point, private key as the scalar, both base64url.
    $script = @"
const { webcrypto } = require('node:crypto');
(async () => {
  const pair = await webcrypto.subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, ['sign']);
  const raw = Buffer.from(await webcrypto.subtle.exportKey('raw', pair.publicKey)).toString('base64url');
  const jwk = await webcrypto.subtle.exportKey('jwk', pair.privateKey);
  console.log(raw + ' ' + jwk.d);
})();
"@
    $keys = (& node -e $script).Trim().Split(' ')
    if ($LASTEXITCODE -ne 0 -or $keys.Count -ne 2) {
        throw "Could not create the keys with Node."
    }
    $secrets = [ordered]@{
        VAPID_PUBLIC_KEY  = $keys[0]
        VAPID_PRIVATE_KEY = $keys[1]
        VAPID_SUBJECT     = $Subject
    }

    if ($Local) {
        $devVars = Join-Path $repoRoot ".dev.vars"
        $lines = if (Test-Path $devVars) { @(Get-Content $devVars | Where-Object { $_ -notmatch '^VAPID_' }) } else { @() }
        $lines += $secrets.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }
        Set-Content -Path $devVars -Value $lines
        Copy-Item $devVars (Join-Path $repoRoot "workers\challenge-cron\.dev.vars") -Force
        Write-Host "Wrote the keys to .dev.vars (and workers\challenge-cron\.dev.vars) for local development."
        return
    }

    Write-Host "Deploying the challenge Worker so it can hold secrets..."
    & npx wrangler deploy --config workers/challenge-cron/wrangler.toml
    if ($LASTEXITCODE -ne 0) { throw "Worker deploy failed. The API token needs 'Workers Scripts: Edit'." }

    foreach ($entry in $secrets.GetEnumerator()) {
        Write-Host "Setting $($entry.Key) on Pages project '$ProjectName'..."
        $entry.Value | & npx wrangler pages secret put $entry.Key --project-name $ProjectName
        if ($LASTEXITCODE -ne 0) { throw "Could not set $($entry.Key) on the Pages project." }

        Write-Host "Setting $($entry.Key) on the challenge Worker..."
        $entry.Value | & npx wrangler secret put $entry.Key --config workers/challenge-cron/wrangler.toml
        if ($LASTEXITCODE -ne 0) { throw "Could not set $($entry.Key) on the Worker." }
    }

    Set-Content -Path $marker -Value (Get-Date -Format "yyyy-MM-dd HH:mm")
    Write-Host "Done. Redeploy the Pages app (push to main or run the deploy workflow) so the new secrets take effect."
}
finally {
    Pop-Location
}
