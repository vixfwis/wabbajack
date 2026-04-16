$ErrorActionPreference = 'Stop'

$repoRoot   = $PSScriptRoot
$publishDir = if ($env:PUBLISH_DIR) { $env:PUBLISH_DIR } else { "E:\WJLocal" }

# Read version from CHANGELOG so assembly metadata is sane, but this build
# is published to a flat folder whose name is NOT a Version string, so
# LauncherUpdater.CommonFolder detects it is outside the standard install
# layout and skips the update check entirely (see LauncherUpdater.cs:67).
$changelogPath = Join-Path $repoRoot "CHANGELOG.md"
$content = Get-Content $changelogPath -Raw
if ($content -match '(?m)^#### Version - ([\d.]+) - ') {
    $version = $Matches[1]
} else {
    $version = "0.0.0.1"
}
Write-Host "Building version: $version (local, no auto-update)" -ForegroundColor Cyan

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

$commonArgs = @(
    "--framework", "net9.0-windows",
    "--runtime",   "win-x64",
    "--configuration", "Release",
    "/p:IncludeNativeLibrariesForSelfExtract=true",
    "--self-contained",
    "/p:DebugType=embedded",
    "/p:VERSION=$version"
)

Push-Location $repoRoot

Write-Host "`nPublishing Wabbajack.App.Wpf..." -ForegroundColor Yellow
dotnet publish Wabbajack.App.Wpf\Wabbajack.App.Wpf.csproj @commonArgs -o $publishDir
if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: Wabbajack.App.Wpf" -ForegroundColor Red; exit 1 }

Write-Host "`nPublishing Wabbajack.CLI..." -ForegroundColor Yellow
dotnet publish Wabbajack.CLI\Wabbajack.CLI.csproj @commonArgs -o "$publishDir\cli"
if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: Wabbajack.CLI" -ForegroundColor Red; exit 1 }

Pop-Location

Write-Host "`nDone!  Output: $publishDir\Wabbajack.exe" -ForegroundColor Green
Write-Host "Auto-update is suppressed: the exe is not inside a version-named subfolder," -ForegroundColor DarkGray
Write-Host "so LauncherUpdater exits early without checking GitHub." -ForegroundColor DarkGray
