# Setup script for Avalonia UI & Markdown packages
# Run this script in PowerShell from the project root: D:\CODE\quick_notes

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " QuickNote - Avalonia UI Package Setup   " -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# 1. Test NuGet Connectivity
Write-Host "`n[1/3] Testing connection to nuget.org..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "https://api.nuget.org/v3/index.json" -TimeoutSec 10 -UseBasicParsing
    if ($response.StatusCode -eq 200) {
        Write-Host "Connection to nuget.org OK!" -ForegroundColor Green
    }
}
catch {
    Write-Warning "Could not connect to nuget.org directly. If you use a VPN or proxy, please ensure it is active."
}

# 2. Restore Shared Avalonia Project
Write-Host "`n[2/3] Restoring QuickNote.Avalonia (Shared UI)..." -ForegroundColor Yellow
dotnet restore src/QuickNote.Avalonia/QuickNote.Avalonia/QuickNote.Avalonia.csproj -v minimal

if ($LASTEXITCODE -eq 0) {
    Write-Host "QuickNote.Avalonia restored successfully!" -ForegroundColor Green
} else {
    Write-Host "Failed to restore QuickNote.Avalonia. Please check network/VPN." -ForegroundColor Red
    exit 1
}

# 3. Restore Desktop Project
Write-Host "`n[3/3] Restoring QuickNote.Avalonia.Desktop (Windows/Linux/Mac runner)..." -ForegroundColor Yellow
dotnet restore src/QuickNote.Avalonia/QuickNote.Avalonia.Desktop/QuickNote.Avalonia.Desktop.csproj -v normal

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nDesktop runner restored successfully!" -ForegroundColor Green
    Write-Host "Building desktop application..." -ForegroundColor Yellow
    dotnet build src/QuickNote.Avalonia/QuickNote.Avalonia.Desktop/QuickNote.Avalonia.Desktop.csproj
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n=========================================" -ForegroundColor Green
        Write-Host " All Avalonia packages installed & built! " -ForegroundColor Green
        Write-Host "=========================================" -ForegroundColor Green
    }
} else {
    Write-Host "Desktop restore encountered an issue. See errors above." -ForegroundColor Red
}
