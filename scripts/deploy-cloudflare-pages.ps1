param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectName = "",

    [Parameter(Mandatory = $false)]
    [string]$Branch = "main"
)

$ErrorActionPreference = "Stop"

if (-not $env:CLOUDFLARE_API_TOKEN) {
    throw "Missing CLOUDFLARE_API_TOKEN environment variable."
}

if (-not $env:CLOUDFLARE_ACCOUNT_ID) {
    throw "Missing CLOUDFLARE_ACCOUNT_ID environment variable."
}

if ([string]::IsNullOrWhiteSpace($ProjectName)) {
    if ($env:CLOUDFLARE_PAGES_PROJECT_NAME) {
        $ProjectName = $env:CLOUDFLARE_PAGES_PROJECT_NAME
    } else {
        throw "Missing project name. Pass -ProjectName or set CLOUDFLARE_PAGES_PROJECT_NAME."
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$webProject = Join-Path $repoRoot "YahtzeeSim.Web\YahtzeeSim.Web.csproj"
$publishRoot = Join-Path $repoRoot "YahtzeeSim.Web\bin\Release\net9.0\publish\wwwroot"

Write-Host "Publishing Blazor app..."
dotnet publish $webProject -c Release

Write-Host "Deploying to Cloudflare Pages project '$ProjectName'..."
npx -y wrangler pages deploy $publishRoot --project-name $ProjectName --branch $Branch

Write-Host "Deployment complete."
