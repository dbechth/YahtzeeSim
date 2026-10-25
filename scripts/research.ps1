<#
.SYNOPSIS
    Starts the YahtzeeSim Research desktop app (Windows).

.DESCRIPTION
    The app pulls data with scripts/export-game-data.ps1 (needs Node, npm ci and `npx wrangler login`), runs the
    strategy analysis for every player and shows the full report, the play-style story each player sees, and the
    badge metrics used to calibrate the badges. Reports are also saved to exports/analysis/ (git-ignored).
#>
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
& dotnet run --project (Join-Path $repoRoot "YahtzeeSim.Research\YahtzeeSim.Research.csproj") -c Release
if ($LASTEXITCODE -ne 0) {
    throw "The research app failed to start (exit code $LASTEXITCODE)."
}
