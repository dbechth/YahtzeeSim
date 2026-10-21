<#
.SYNOPSIS
    Analyses Full Sheet strategy from the exported game data and writes an HTML report.

.DESCRIPTION
    Rule-based and deterministic: every finished Full Sheet game in exports/ is replayed from its recorded actions,
    each decision is graded against an exact-solver reference strategy, and findings come only from the fixed rule
    table in YahtzeeSim.Analysis/Rules/StrategyRules.cs. The same export and seed always give the same report.

    Reports are written to exports/analysis/ (git-ignored, personal data):
      index.html          overview of all players, method and rules
      <player>.html       one page per player
      <player>.md         the same findings as Markdown
      decisions.csv       every graded decision

    Run scripts/export-game-data.ps1 first to download fresh data.

.PARAMETER ExportsDir
    Folder with users.csv, scores.csv and actions.csv. Defaults to <repo>\exports.

.PARAMETER OutputDir
    Report folder. Defaults to <ExportsDir>\analysis.

.PARAMETER Simulations
    Simulated games per baseline strategy (reference and greedy).

.PARAMETER Seed
    Seed for the simulated dice.

.PARAMETER NoCache
    Recompute the single-card solver table (a few seconds) instead of reusing <OutputDir>\cache.

.PARAMETER Open
    Open index.html when done.

.EXAMPLE
    .\scripts\analyze-strategy.ps1
    .\scripts\analyze-strategy.ps1 -Open
    .\scripts\analyze-strategy.ps1 -Simulations 5000
#>
param(
    [string]$ExportsDir = "",
    [string]$OutputDir = "",
    [ValidateRange(1, 1000000)]
    [int]$Simulations = 1000,
    [int]$Seed = 20261020,
    [switch]$NoCache,
    [switch]$Open
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExportsDir)) {
    $ExportsDir = Join-Path $repoRoot "exports"
}
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $ExportsDir "analysis"
}

foreach ($file in "scores.csv", "actions.csv") {
    if (-not (Test-Path (Join-Path $ExportsDir $file))) {
        throw "Missing $file in $ExportsDir. Run .\scripts\export-game-data.ps1 first."
    }
}

$project = Join-Path $repoRoot "YahtzeeSim.Analysis.Cli\YahtzeeSim.Analysis.Cli.csproj"
$cliArgs = @("--exports", $ExportsDir, "--out", $OutputDir, "--simulations", $Simulations, "--seed", $Seed)
if ($NoCache) {
    $cliArgs += "--no-cache"
}

& dotnet run --project $project -c Release -- @cliArgs
if ($LASTEXITCODE -ne 0) {
    throw "Strategy analysis failed (exit code $LASTEXITCODE)."
}

$index = Join-Path $OutputDir "index.html"
if ($Open -and (Test-Path $index)) {
    Start-Process $index
}
