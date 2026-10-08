<#
.SYNOPSIS
    Builds POTA Activation Tool.

.DESCRIPTION
    Publishes a self-contained, single-file exe (no .NET runtime needed on
    the target PC) and, if Inno Setup is installed, compiles the installer.

.PARAMETER Version
    Version stamped into the exe and the installer filename.

.PARAMETER SkipInstaller
    Publish the exe only; do not build the installer.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Version 1.4.0
#>

[CmdletBinding()]
param(
    [string]$Version = "1.6.0",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "POTA Activation Tool.csproj"
$publishDir = Join-Path $root "publish"
$distDir = Join-Path $root "dist"

Write-Host ""
Write-Host "POTA Activation Tool - build v$Version" -ForegroundColor Cyan
Write-Host ("=" * 55)

# --- 1. Publish --------------------------------------------------------

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Write-Host ""
Write-Host "Publishing self-contained exe..." -ForegroundColor Yellow

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -p:FileVersion="$Version.0" `
    -p:AssemblyVersion="$Version.0" `
    -o $publishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$exe = Join-Path $publishDir "POTA Activation Tool.exe"
if (-not (Test-Path $exe)) { throw "Published exe not found at $exe" }

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "  $exe  ($sizeMb MB)" -ForegroundColor Green

# --- 2. Installer --------------------------------------------------------

if ($SkipInstaller) {
    Write-Host ""
    Write-Host "Skipping installer (-SkipInstaller)." -ForegroundColor DarkGray
    exit 0
}

$iscc = @(
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host ""
    Write-Host "Inno Setup was not found, so no installer was built." -ForegroundColor Yellow
    Write-Host "Install it from https://jrsoftware.org/isdl.php and run this again," -ForegroundColor Yellow
    Write-Host "or just distribute publish\POTA Activation Tool.exe on its own." -ForegroundColor Yellow
    exit 0
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null

Write-Host ""
Write-Host "Building installer..." -ForegroundColor Yellow

& $iscc "/DMyAppVersion=$Version" (Join-Path $root "Installer\InnoSetup\POTA Activation Tool.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }

$setup = Join-Path $distDir "POTA Activation Tool Setup $Version.exe"
if (Test-Path $setup) {
    $setupMb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
    Write-Host ""
    Write-Host "  $setup  ($setupMb MB)" -ForegroundColor Green
}

Write-Host ""
Write-Host "Done." -ForegroundColor Cyan
Write-Host ""
