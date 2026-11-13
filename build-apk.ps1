# Build script to generate Android APK package for QuickNote
$ErrorActionPreference = "Stop"

$RootDir = $PSScriptRoot
if (-not $RootDir) { $RootDir = (Get-Location).Path }

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host " Building QuickNote Android APK Package " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan

# Set environment fallbacks if not already in session
if (-not $env:ANDROID_HOME -or -not (Test-Path $env:ANDROID_HOME)) {
    $defaultAndroid = "$env:LOCALAPPDATA\Android\Sdk"
    if (Test-Path $defaultAndroid) {
        $env:ANDROID_HOME = $defaultAndroid
    }
}

if (-not $env:JAVA_HOME -or -not (Test-Path $env:JAVA_HOME)) {
    $defaultJava = "C:\Program Files\Android\Android Studio\jbr"
    if (Test-Path $defaultJava) {
        $env:JAVA_HOME = $defaultJava
    }
}

Write-Host "ANDROID_HOME: $env:ANDROID_HOME" -ForegroundColor DarkGray
Write-Host "JAVA_HOME:    $env:JAVA_HOME" -ForegroundColor DarkGray

$DistDir = Join-Path $RootDir "dist"
if (-not (Test-Path $DistDir)) {
    New-Item -ItemType Directory -Path $DistDir -Force | Out-Null
}

$AndroidProj = Join-Path $RootDir "src/QuickNote.Avalonia/QuickNote.Avalonia.Android/QuickNote.Avalonia.Android.csproj"

Write-Host "`n[1/2] Publishing QuickNote Android (Debug signed APK)..." -ForegroundColor Yellow
dotnet publish $AndroidProj -c Debug -f net10.0-android

# Locate signed APK
$PublishDir = Join-Path $RootDir "src/QuickNote.Avalonia/QuickNote.Avalonia.Android/bin/Debug/net10.0-android/publish"
$SignedApk = Get-ChildItem -Path $PublishDir -Filter "*Signed.apk" | Select-Object -First 1

if (-not $SignedApk) {
    # Check bin directory recursively if publish folder didn't have it directly
    $SignedApk = Get-ChildItem -Path (Join-Path $RootDir "src/QuickNote.Avalonia/QuickNote.Avalonia.Android/bin") -Filter "*Signed.apk" -Recurse | Select-Object -First 1
}

if (-not $SignedApk) {
    throw "Build failed: Signed APK was not found in $PublishDir"
}

Write-Host "`n[2/2] Copying APK to dist folder..." -ForegroundColor Yellow
$DestApk = Join-Path $DistDir "QuickNote.apk"
Copy-Item -Path $SignedApk.FullName -Destination $DestApk -Force
# Also copy with full package name for convenience
$DestSigned = Join-Path $DistDir $SignedApk.Name
Copy-Item -Path $SignedApk.FullName -Destination $DestSigned -Force

$ApkInfo = Get-Item $DestApk
Write-Host "`n=================================================" -ForegroundColor Green
Write-Host " Android APK Exported Successfully!" -ForegroundColor Green
Write-Host " Location: $DestApk" -ForegroundColor Green
Write-Host " Package:  $DestSigned" -ForegroundColor Green
Write-Host " Size:     $([math]::Round($ApkInfo.Length / 1MB, 2)) MB" -ForegroundColor Green
Write-Host "=================================================" -ForegroundColor Green
