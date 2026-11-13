# Master script to export both Windows Installer (.exe) and Android App (.apk)
$ErrorActionPreference = "Stop"

$RootDir = $PSScriptRoot
if (-not $RootDir) { $RootDir = (Get-Location).Path }

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Exporting QuickNote Packages (Windows + Android)" -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan

# 1. Build Windows Installer
Write-Host "`n>>> Step 1: Building Windows Installer..." -ForegroundColor Magenta
& (Join-Path $RootDir "build-installer.ps1")

# 2. Build Android APK
Write-Host "`n>>> Step 2: Building Android APK..." -ForegroundColor Magenta
& (Join-Path $RootDir "build-apk.ps1")

Write-Host "`n=================================================" -ForegroundColor Green
Write-Host " All Packages Exported Successfully to 'dist\'!" -ForegroundColor Green
Write-Host "=================================================" -ForegroundColor Green
Get-ChildItem -Path (Join-Path $RootDir "dist") | Format-Table Name, @{N='Size (MB)';E={[math]::Round($_.Length/1MB, 2)}}, LastWriteTime
