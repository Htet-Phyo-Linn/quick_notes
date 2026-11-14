param(
    [string]$Configuration = "Release",
    [switch]$Install
)

$ErrorActionPreference = "Stop"

$RootDir = $PSScriptRoot
if (-not $RootDir) { $RootDir = (Get-Location).Path }

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host " QuickNote Android APK Export ($Configuration)   " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan

# Set environment fallbacks if not already set
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

Write-Host "`n[1/3] Publishing Android App ($Configuration)..." -ForegroundColor Yellow
dotnet publish $AndroidProj -c $Configuration -f net10.0-android

# Find signed APK in publish or bin directory
$SearchDirs = @(
    (Join-Path $RootDir "src/QuickNote.Avalonia/QuickNote.Avalonia.Android/bin/$Configuration/net10.0-android/publish"),
    (Join-Path $RootDir "src/QuickNote.Avalonia/QuickNote.Avalonia.Android/bin/$Configuration/net10.0-android"),
    (Join-Path $RootDir "src/QuickNote.Avalonia/QuickNote.Avalonia.Android/bin")
)

$SignedApk = $null
foreach ($dir in $SearchDirs) {
    if (Test-Path $dir) {
        $SignedApk = Get-ChildItem -Path $dir -Filter "*Signed.apk" -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($SignedApk) { break }
    }
}

if (-not $SignedApk) {
    # Fallback to any .apk file generated
    foreach ($dir in $SearchDirs) {
        if (Test-Path $dir) {
            $SignedApk = Get-ChildItem -Path $dir -Filter "*.apk" -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($SignedApk) { break }
        }
    }
}

if (-not $SignedApk) {
    throw "Build failed: APK file was not found."
}

Write-Host "`n[2/3] Exporting APK to dist folder..." -ForegroundColor Yellow
$DestApk = Join-Path $DistDir "QuickNote.apk"
Copy-Item -Path $SignedApk.FullName -Destination $DestApk -Force

$DestSigned = Join-Path $DistDir $SignedApk.Name
Copy-Item -Path $SignedApk.FullName -Destination $DestSigned -Force

$ApkInfo = Get-Item $DestApk
Write-Host "`n=================================================" -ForegroundColor Green
Write-Host " Android APK Exported Successfully!" -ForegroundColor Green
Write-Host " Output File: $DestApk" -ForegroundColor Green
Write-Host " Package:     $DestSigned" -ForegroundColor Green
Write-Host " Size:        $([math]::Round($ApkInfo.Length / 1MB, 2)) MB" -ForegroundColor Green
Write-Host " Modified:    $($ApkInfo.LastWriteTime)" -ForegroundColor Green
Write-Host "=================================================" -ForegroundColor Green

# Optional: Install to connected Android device via ADB
if ($Install) {
    Write-Host "`n[3/3] Installing APK to connected device via ADB..." -ForegroundColor Yellow
    adb install -r $DestApk
    Write-Host "Installed successfully! Launching app..." -ForegroundColor Green
    adb shell monkey -p com.quicknote.android -c android.intent.category.LAUNCHER 1
}
