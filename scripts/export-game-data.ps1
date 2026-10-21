<#
.SYNOPSIS
    Downloads users, scores and game actions from the D1 database into the exports/ folder (JSON + CSV).

.DESCRIPTION
    Uses `wrangler d1 execute` against the DB binding in wrangler.toml. Requires `npm install` and, for the
    remote database, `npx wrangler login` or CLOUDFLARE_API_TOKEN / CLOUDFLARE_ACCOUNT_ID.
    Output is personal gameplay data: exports/ is git-ignored, never commit it.

.PARAMETER Local
    Read the local SQLite copy under .wrangler/ (used by `wrangler pages dev`) instead of the remote database.

.PARAMETER OutputDir
    Destination folder. Defaults to <repo>\exports.

.PARAMETER PageSize
    Rows fetched per request, to stay under D1 result limits.

.EXAMPLE
    .\scripts\export-game-data.ps1
    .\scripts\export-game-data.ps1 -Local
#>
param(
    [switch]$Local,
    [string]$OutputDir = "",
    [ValidateRange(1, 100000)]
    [int]$PageSize = 5000
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "exports"
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$target = if ($Local) { "--local" } else { "--remote" }
$utf8 = New-Object System.Text.UTF8Encoding($false)

# Each table is paged by its key (keyset pagination) so rows are never skipped or repeated.
$tables = @(
    @{
        Name = "users"
        Key  = "id"
        Sql  = "SELECT id, nickname, created_at FROM users {0} ORDER BY id LIMIT $PageSize"
    },
    @{
        Name = "scores"
        Key  = "id"
        Sql  = "SELECT s.id, u.nickname, s.user_id, s.client_game_id, s.mode, s.total, s.played_at FROM scores s JOIN users u ON u.id = s.user_id {0} ORDER BY s.id LIMIT $PageSize"
        KeyColumn = "s.id"
    },
    @{
        Name = "actions"
        Key  = "id"
        Sql  = "SELECT a.id, u.nickname, a.user_id, a.client_game_id, a.seq, a.mode, a.action, a.occurred_at, a.received_at, a.turn, a.roll_number, a.dice, a.die_id, a.game_index, a.category, a.points, a.yahtzee_bonus FROM game_actions a JOIN users u ON u.id = a.user_id {0} ORDER BY a.id LIMIT $PageSize"
        KeyColumn = "a.id"
    }
)

function Invoke-D1Query([string]$Sql) {
    Push-Location $repoRoot
    try {
        $output = & npx wrangler d1 execute DB $target --json --command $Sql 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "wrangler failed:`n$($output -join "`n")"
        }
    } finally {
        Pop-Location
    }

    # Wrangler can print banners or warnings ahead of the JSON payload.
    $lines = @($output | ForEach-Object { "$_" })
    $start = [array]::FindIndex($lines, [Predicate[string]] { param($l) $l.TrimStart().StartsWith("[") })
    if ($start -lt 0) {
        throw "Unexpected wrangler output:`n$($lines -join "`n")"
    }
    $parsed = ($lines[$start..($lines.Count - 1)] -join "`n") | ConvertFrom-Json
    $result = @($parsed)[0]
    if (-not $result.success) {
        throw "Query failed: $Sql"
    }
    return $result
}

function ConvertTo-CsvField($Value) {
    if ($null -eq $Value) { return "" }
    $text = [string]$Value
    if ($text -match '[",\r\n]') {
        return '"' + $text.Replace('"', '""') + '"'
    }
    return $text
}

foreach ($table in $tables) {
    $rows = New-Object System.Collections.Generic.List[object]
    $lastKey = $null
    $keyColumn = if ($table.KeyColumn) { $table.KeyColumn } else { $table.Key }

    while ($true) {
        # Integer keys are compared numerically; text keys (users.id) as quoted strings.
        $where = if ($null -eq $lastKey) { "" }
            elseif ($lastKey -is [ValueType]) { "WHERE $keyColumn > $lastKey" }
            else { "WHERE $keyColumn > '$(([string]$lastKey).Replace("'", "''"))'" }

        $page = @((Invoke-D1Query ($table.Sql -f $where)).results)
        foreach ($row in $page) { $rows.Add($row) }
        if ($page.Count -lt $PageSize) { break }
        $lastKey = $page[-1].($table.Key)
    }

    $jsonPath = Join-Path $OutputDir "$($table.Name).json"
    $csvPath = Join-Path $OutputDir "$($table.Name).csv"

    $json = ConvertTo-Json -InputObject @(@{ results = $rows.ToArray(); success = $true }) -Depth 5
    [IO.File]::WriteAllText($jsonPath, $json, $utf8)

    $csv = New-Object System.Text.StringBuilder
    if ($rows.Count -gt 0) {
        $columns = @($rows[0].PSObject.Properties.Name)
        [void]$csv.Append(($columns -join ",")).Append("`n")
        foreach ($row in $rows) {
            $fields = foreach ($c in $columns) { ConvertTo-CsvField $row.$c }
            [void]$csv.Append(($fields -join ",")).Append("`n")
        }
    }
    [IO.File]::WriteAllText($csvPath, $csv.ToString(), $utf8)

    Write-Host ("{0,-8} {1,7} rows -> {2}" -f $table.Name, $rows.Count, $csvPath)
}

Write-Host "Export complete: $OutputDir"
