# QuickNote Mobile Simulator Launcher
# Launches the app on Windows in an Android 412x869 phone viewport

$DesktopExe = Join-Path $PSScriptRoot "src\QuickNote.Avalonia\QuickNote.Avalonia.Desktop\bin\Debug\net10.0\QuickNote.exe"

if (-not (Test-Path $DesktopExe)) {
    Write-Host "Building QuickNote Desktop..." -ForegroundColor Cyan
    dotnet build (Join-Path $PSScriptRoot "src\QuickNote.Avalonia\QuickNote.Avalonia.Desktop\QuickNote.Avalonia.Desktop.csproj") -c Debug
}

Write-Host "Starting QuickNote Mobile Simulator (Android 412x869)..." -ForegroundColor Green
Start-Process -FilePath $DesktopExe -ArgumentList "--mobile"
