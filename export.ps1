<#
.SYNOPSIS
    QuickNote Unified Export Script (Windows Installer + Android APK)
.DESCRIPTION
    Exports production-ready binaries into the 'dist/' folder.
    Supports building Windows Single-File Installer (QuickNoteSetup.exe) and Android APK (QuickNote.apk).
.PARAMETER Target
    The target platform to export: "All" (default), "Windows", or "Android".
.PARAMETER Configuration
    Build configuration: "Release" (default) or "Debug".
.EXAMPLE
    .\export.ps1
    Exports both Windows installer and Android APK.
.EXAMPLE
    .\export.ps1 -Target Windows
    Exports only the Windows single-file installer.
.EXAMPLE
    .\export.ps1 -Target Android
    Exports only the Android APK package.
#>
param(
    [ValidateSet("All", "Windows", "Desktop", "Win", "Android", "Apk", "Mobile")]
    [string]$Target = "All",

    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [ValidateSet("arm64", "universal", "all")]
    [string]$AndroidArch = "arm64"
)

$ErrorActionPreference = "Stop"

$RootDir = $PSScriptRoot
if (-not $RootDir) { $RootDir = (Get-Location).Path }

$DistDir = Join-Path $RootDir "dist"
if (-not (Test-Path $DistDir)) {
    New-Item -ItemType Directory -Path $DistDir -Force | Out-Null
}

function Build-WindowsInstaller {
    param([string]$Config = "Release")

    Write-Host "`n=================================================" -ForegroundColor Cyan
    Write-Host " [1] Exporting Windows Single-File Installer ($Config)" -ForegroundColor Cyan
    Write-Host "=================================================" -ForegroundColor Cyan

    $StagingApp  = Join-Path $RootDir "bin/staging/app"
    $PayloadZip  = Join-Path $RootDir "src/QuickNote.Installer/Resources/payload.zip"
    $DesktopProj = Join-Path $RootDir "src/QuickNote.Avalonia/QuickNote.Avalonia.Desktop/QuickNote.Avalonia.Desktop.csproj"
    $InstallerProj = Join-Path $RootDir "src/QuickNote.Installer/QuickNote.Installer.csproj"
    $SetupExe    = Join-Path $DistDir "QuickNoteSetup.exe"

    # 1. Clean previous staging & dist binaries
    Write-Host "[1/4] Cleaning previous staging files..." -ForegroundColor Yellow
    if (Test-Path $StagingApp) { Remove-Item -Recurse -Force $StagingApp }
    if (Test-Path $PayloadZip) { Remove-Item -Force $PayloadZip }
    if (Test-Path $SetupExe) { Remove-Item -Force $SetupExe }
    $Pdb = Join-Path $DistDir "QuickNoteSetup.pdb"
    if (Test-Path $Pdb) { Remove-Item -Force $Pdb }

    # 2. Publish QuickNote Desktop Application (self-contained win-x64)
    Write-Host "[2/4] Publishing QuickNote Desktop (Avalonia + Milkdown)..." -ForegroundColor Yellow
    dotnet publish $DesktopProj -c $Config -r win-x64 --self-contained true -p:PublishSingleFile=false -o $StagingApp
    if ($LASTEXITCODE -ne 0) { throw "Desktop publish failed with exit code $LASTEXITCODE" }

    if (-not (Test-Path (Join-Path $StagingApp "QuickNote.exe"))) {
        throw "Build failed: QuickNote.exe was not produced in $StagingApp"
    }

    # 3. Compress application payload
    Write-Host "[3/4] Compressing payload into installer resource..." -ForegroundColor Yellow
    $ResourceDir = Split-Path -Parent $PayloadZip
    if (-not (Test-Path $ResourceDir)) { New-Item -ItemType Directory -Path $ResourceDir -Force | Out-Null }
    Compress-Archive -Path "$StagingApp/*" -DestinationPath $PayloadZip -CompressionLevel Optimal

    $ZipInfo = Get-Item $PayloadZip
    Write-Host "      Compressed payload size: $([math]::Round($ZipInfo.Length / 1MB, 2)) MB" -ForegroundColor Green

    # 4. Build single-file installer
    Write-Host "[4/4] Compiling standalone QuickNoteSetup.exe..." -ForegroundColor Yellow
    dotnet publish $InstallerProj -c $Config -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $DistDir
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed with exit code $LASTEXITCODE" }

    # Clean temporary files
    if (Test-Path $Pdb) { Remove-Item -Force $Pdb }
    if (Test-Path $StagingApp) { Remove-Item -Recurse -Force (Join-Path $RootDir "bin/staging") }

    if (Test-Path $SetupExe) {
        $ExeInfo = Get-Item $SetupExe
        Write-Host "-------------------------------------------------" -ForegroundColor Green
        Write-Host " [SUCCESS] Windows Installer: $SetupExe" -ForegroundColor Green
        Write-Host " Size: $([math]::Round($ExeInfo.Length / 1MB, 2)) MB" -ForegroundColor Green
        Write-Host "-------------------------------------------------" -ForegroundColor Green
    } else {
        throw "Failed to locate generated installer: $SetupExe"
    }
}

function Build-AndroidApk {
    param([string]$Config = "Debug")

    Write-Host "`n=================================================" -ForegroundColor Cyan
    Write-Host " [2] Exporting Android APK Package ($Config)" -ForegroundColor Cyan
    Write-Host "=================================================" -ForegroundColor Cyan

    # Environment fallbacks
    if (-not $env:ANDROID_HOME -or -not (Test-Path $env:ANDROID_HOME)) {
        $defaultAndroid = "$env:LOCALAPPDATA\Android\Sdk"
        if (Test-Path $defaultAndroid) { $env:ANDROID_HOME = $defaultAndroid }
    }

    if (-not $env:JAVA_HOME -or -not (Test-Path $env:JAVA_HOME)) {
        $defaultJava = "C:\Program Files\Android\Android Studio\jbr"
        if (Test-Path $defaultJava) { $env:JAVA_HOME = $defaultJava }
    }

    Write-Host "ANDROID_HOME: $env:ANDROID_HOME" -ForegroundColor DarkGray
    Write-Host "JAVA_HOME:    $env:JAVA_HOME" -ForegroundColor DarkGray

    $AndroidProj = Join-Path $RootDir "src/QuickNote.Avalonia/QuickNote.Avalonia.Android/QuickNote.Avalonia.Android.csproj"
    Write-Host "[1/2] Publishing Android App ($Config, Arch: $AndroidArch)..." -ForegroundColor Yellow
    if ($AndroidArch -eq "arm64") {
        dotnet publish $AndroidProj -c $Config -f net10.0-android -r android-arm64
    } else {
        dotnet publish $AndroidProj -c $Config -f net10.0-android
    }
    if ($LASTEXITCODE -ne 0) { throw "Android publish failed with exit code $LASTEXITCODE" }

    # Locate generated APK
    Write-Host "[2/2] Locating & copying APK to dist folder..." -ForegroundColor Yellow
    $SearchDirs = @(
        (Join-Path $RootDir "src/QuickNote.Avalonia/QuickNote.Avalonia.Android/bin/$Config/net10.0-android/android-arm64"),
        (Join-Path $RootDir "src/QuickNote.Avalonia/QuickNote.Avalonia.Android/bin/$Config/net10.0-android/publish"),
        (Join-Path $RootDir "src/QuickNote.Avalonia/QuickNote.Avalonia.Android/bin/$Config/net10.0-android"),
        (Join-Path $RootDir "src/QuickNote.Avalonia/QuickNote.Avalonia.Android/bin")
    )

    $FoundApk = $null
    foreach ($dir in $SearchDirs) {
        if (Test-Path $dir) {
            $FoundApk = Get-ChildItem -Path $dir -Filter "*Signed.apk" -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($FoundApk) { break }
        }
    }

    if (-not $FoundApk) {
        foreach ($dir in $SearchDirs) {
            if (Test-Path $dir) {
                $FoundApk = Get-ChildItem -Path $dir -Filter "*.apk" -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
                if ($FoundApk) { break }
            }
        }
    }

    if (-not $FoundApk) {
        throw "Android export failed: No .apk package found in output directories"
    }

    $DestApk = Join-Path $DistDir "QuickNote.apk"
    Copy-Item -Path $FoundApk.FullName -Destination $DestApk -Force

    $ApkInfo = Get-Item $DestApk
    Write-Host "-------------------------------------------------" -ForegroundColor Green
    Write-Host " [SUCCESS] Android APK: $DestApk" -ForegroundColor Green
    Write-Host " Original: $($FoundApk.Name)" -ForegroundColor Green
    Write-Host " Size:     $([math]::Round($ApkInfo.Length / 1MB, 2)) MB" -ForegroundColor Green
    Write-Host "-------------------------------------------------" -ForegroundColor Green
}

# ── Execute Selected Target ──────────────────────────────────────────────────
$sw = [System.Diagnostics.Stopwatch]::StartNew()

switch ($Target) {
    { $_ -in "Windows", "Desktop", "Win" } {
        Build-WindowsInstaller -Config $Configuration
    }
    { $_ -in "Android", "Apk", "Mobile" } {
        Build-AndroidApk -Config $Configuration
    }
    default {
        # Build All
        Build-WindowsInstaller -Config $Configuration
        Build-AndroidApk -Config $Configuration
    }
}

$sw.Stop()

Write-Host "`n=================================================" -ForegroundColor Green
Write-Host " Export Complete! (Total time: $($sw.Elapsed.ToString('mm\:ss')))" -ForegroundColor Green
Write-Host " Output Directory: $DistDir" -ForegroundColor Green
Write-Host "=================================================" -ForegroundColor Green
Get-ChildItem -Path $DistDir -Filter "*.*" | Where-Object { $_.Extension -in ".exe", ".apk" } |
    Format-Table Name, @{N='Size (MB)';E={[math]::Round($_.Length/1MB, 2)}}, LastWriteTime
