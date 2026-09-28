# Build script to generate a single-file QuickNote installation package
$ErrorActionPreference = "Stop"

$RootDir = $PSScriptRoot
if (-not $RootDir) { $RootDir = (Get-Location).Path }

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host " Building QuickNote Single-File Installer " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan

$StagingApp = Join-Path $RootDir "bin/staging/app"
$PayloadZip = Join-Path $RootDir "src/QuickNote.Installer/Resources/payload.zip"
$DistDir    = Join-Path $RootDir "dist"

# 1. Clean previous build artifacts
Write-Host "`n[1/4] Cleaning previous staging directories..." -ForegroundColor Yellow
if (Test-Path $StagingApp) { Remove-Item -Recurse -Force $StagingApp }
if (Test-Path $PayloadZip) { Remove-Item -Force $PayloadZip }
if (Test-Path $DistDir)    { Remove-Item -Recurse -Force $DistDir }

# 2. Publish QuickNote Desktop Application (self-contained win-x64)
Write-Host "`n[2/4] Publishing QuickNote Desktop (Avalonia + Milkdown)..." -ForegroundColor Yellow
$DesktopProj = Join-Path $RootDir "src/QuickNote.Avalonia/QuickNote.Avalonia.Desktop/QuickNote.Avalonia.Desktop.csproj"
dotnet publish $DesktopProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $StagingApp

if (-not (Test-Path (Join-Path $StagingApp "QuickNote.exe"))) {
    throw "Build failed: QuickNote.exe was not produced in $StagingApp"
}

# 3. Compress application payload
Write-Host "`n[3/4] Compressing application payload into installer resource..." -ForegroundColor Yellow
Compress-Archive -Path "$StagingApp/*" -DestinationPath $PayloadZip -CompressionLevel Optimal

$ZipInfo = Get-Item $PayloadZip
Write-Host "      Payload compressed: $([math]::Round($ZipInfo.Length / 1MB, 2)) MB" -ForegroundColor Green

# 4. Build single-file installer executable
Write-Host "`n[4/4] Building standalone QuickNoteSetup.exe installer..." -ForegroundColor Yellow
$InstallerProj = Join-Path $RootDir "src/QuickNote.Installer/QuickNote.Installer.csproj"
dotnet publish $InstallerProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $DistDir

# Clean up PDB in dist
$DistPdb = Join-Path $DistDir "QuickNoteSetup.pdb"
if (Test-Path $DistPdb) { Remove-Item -Force $DistPdb }

# Remove staging directory to save disk space
if (Test-Path $StagingApp) { Remove-Item -Recurse -Force (Join-Path $RootDir "bin/staging") }

$SetupExe = Join-Path $DistDir "QuickNoteSetup.exe"
if (Test-Path $SetupExe) {
    $ExeInfo = Get-Item $SetupExe
    Write-Host "`n=================================================" -ForegroundColor Green
    Write-Host " Installer Generated Successfully!" -ForegroundColor Green
    Write-Host " Location: $SetupExe" -ForegroundColor Green
    Write-Host " Size:     $([math]::Round($ExeInfo.Length / 1MB, 2)) MB" -ForegroundColor Green
    Write-Host "=================================================" -ForegroundColor Green
} else {
    throw "Installer generation failed: $SetupExe not found"
}
