<#
.SYNOPSIS
    Builds the YahtzeeSim Research desktop app and adds Start menu and desktop shortcuts for it.

.DESCRIPTION
    Publishes the app to YahtzeeSim.Research\bin\publish (inside the repository, git-ignored, so the app can find the
    exports folder and the export script) and creates "YahtzeeSim Research" shortcuts. Run it again after pulling code
    changes to update the installed app; the shortcuts stay the same.

.PARAMETER NoDesktop
    Only add the Start menu shortcut.

.PARAMETER Uninstall
    Remove the shortcuts (the published files are left in bin\publish).

.EXAMPLE
    .\scripts\install-research.ps1
    .\scripts\install-research.ps1 -NoDesktop
    .\scripts\install-research.ps1 -Uninstall
#>
param(
    [switch]$NoDesktop,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$name = "YahtzeeSim Research"
$shortcuts = @(Join-Path ([Environment]::GetFolderPath("Programs")) "$name.lnk")
if (-not $NoDesktop) {
    $shortcuts += Join-Path ([Environment]::GetFolderPath("Desktop")) "$name.lnk"
}

if ($Uninstall) {
    foreach ($path in @(
            (Join-Path ([Environment]::GetFolderPath("Programs")) "$name.lnk"),
            (Join-Path ([Environment]::GetFolderPath("Desktop")) "$name.lnk"))) {
        if (Test-Path $path) {
            Remove-Item $path
            Write-Host "Removed $path"
        }
    }
    return
}

$project = Join-Path $repoRoot "YahtzeeSim.Research\YahtzeeSim.Research.csproj"
$publishDir = Join-Path $repoRoot "YahtzeeSim.Research\bin\publish"
$exe = Join-Path $publishDir "YahtzeeSim.Research.exe"

$running = Get-Process -Name "YahtzeeSim.Research" -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($publishDir, [StringComparison]::OrdinalIgnoreCase) }
if ($running) {
    throw "$name is open. Close it, then run this script again."
}

Write-Host "Publishing $name..."
& dotnet publish $project -c Release -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Publishing failed (exit code $LASTEXITCODE)."
}

$shell = New-Object -ComObject WScript.Shell
foreach ($path in $shortcuts) {
    $link = $shell.CreateShortcut($path)
    $link.TargetPath = $exe
    $link.WorkingDirectory = $repoRoot
    $link.IconLocation = "$exe,0"
    $link.Description = "Pull YahtzeeSim game data and review player strategy, stories and badges"
    $link.Save()
    Write-Host "Shortcut: $path"
}

Write-Host "Done. Start '$name' from the Start menu$(if (-not $NoDesktop) { ' or the desktop' })."
