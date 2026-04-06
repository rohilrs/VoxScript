# native/whisper/build.ps1
# Requires: Visual Studio 2022 Build Tools, CMake >= 3.22, Git
# For Vulkan: Vulkan SDK (https://vulkan.lunarg.com/) — runtime is in GPU drivers,
#             but the SDK is needed at build time for headers and loader lib.
param(
    [string]$BuildType = "Release",
    [switch]$Vulkan,
    [switch]$Cuda
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "=== VoxScript whisper.cpp build script ===" -ForegroundColor Cyan

# Check prerequisites
if (-not (Get-Command cmake -ErrorAction SilentlyContinue)) {
    Write-Error "CMake not found. Install CMake >= 3.22 and add to PATH."
    exit 1
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Error "Git not found. Install Git and add to PATH."
    exit 1
}
if ($Vulkan -and -not $env:VULKAN_SDK) {
    Write-Error "VULKAN_SDK environment variable not set. Install the Vulkan SDK from https://vulkan.lunarg.com/"
    exit 1
}

# Clone if not present
if (-not (Test-Path "$root\whisper.cpp")) {
    Write-Host "Cloning whisper.cpp..." -ForegroundColor Yellow
    git clone https://github.com/ggml-org/whisper.cpp.git "$root\whisper.cpp"
    if ($LASTEXITCODE -ne 0) { Write-Error "git clone failed"; exit 1 }
} else {
    Write-Host "Updating whisper.cpp..." -ForegroundColor Yellow
    Push-Location "$root\whisper.cpp"
    git pull
    Pop-Location
}

$buildDir = "$root\whisper.cpp\build-windows"
if (Test-Path $buildDir) {
    Remove-Item -Recurse -Force $buildDir
}
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
Push-Location $buildDir

$cmakeArgs = @(
    "..",
    "-DBUILD_SHARED_LIBS=ON",
    "-DWHISPER_BUILD_EXAMPLES=OFF",
    "-DWHISPER_BUILD_TESTS=OFF"
)

if ($Vulkan) {
    $cmakeArgs += "-DGGML_VULKAN=ON"
    Write-Host "Vulkan GPU acceleration enabled" -ForegroundColor Green
}
if ($Cuda) {
    $cmakeArgs += "-DGGML_CUDA=ON"
    Write-Host "CUDA acceleration enabled" -ForegroundColor Green
}

Write-Host "Running CMake configure..." -ForegroundColor Yellow
Write-Host "cmake $($cmakeArgs -join ' ')" -ForegroundColor Gray
cmake @cmakeArgs
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    Write-Error "CMake configure failed with exit code $LASTEXITCODE"
    exit 1
}

Write-Host "Running CMake build ($BuildType)..." -ForegroundColor Yellow
cmake --build . --config $BuildType --parallel
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    Write-Error "CMake build failed with exit code $LASTEXITCODE"
    exit 1
}

Pop-Location

# Find the built DLLs — path varies by generator (MSVC uses config subdirs, others don't)
$searchPaths = @(
    "$buildDir\bin\$BuildType",
    "$buildDir\bin",
    "$buildDir\src\$BuildType",
    "$buildDir\src",
    "$buildDir\$BuildType"
)

$whisperDll = $null
foreach ($sp in $searchPaths) {
    $candidate = Join-Path $sp "whisper.dll"
    if (Test-Path $candidate) {
        $whisperDll = $candidate
        Write-Host "Found whisper.dll at: $candidate" -ForegroundColor Green
        break
    }
}

if (-not $whisperDll) {
    # Fallback: search recursively
    $found = Get-ChildItem -Path $buildDir -Filter "whisper.dll" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) {
        $whisperDll = $found.FullName
        Write-Host "Found whisper.dll at: $whisperDll" -ForegroundColor Green
    } else {
        Write-Error "whisper.dll not found after build. Check CMake output above."
        exit 1
    }
}

$dllDir = Split-Path $whisperDll

# Copy output DLLs to solution's native bin
$outDir = "$root\..\..\VoxScript\NativeBinaries\x64"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Write-Host "Copying DLLs to $outDir..." -ForegroundColor Yellow
Get-ChildItem -Path $dllDir -Filter "*.dll" | ForEach-Object {
    Copy-Item $_.FullName $outDir -Force
    Write-Host "  Copied: $($_.Name)" -ForegroundColor Gray
}

# Also copy to the app's bin output for immediate use during development
$debugOut = "$root\..\..\VoxScript\bin\Debug\net10.0-windows10.0.19041.0"
if (Test-Path $debugOut) {
    Get-ChildItem -Path $dllDir -Filter "*.dll" | ForEach-Object {
        Copy-Item $_.FullName $debugOut -Force
    }
    Write-Host "Also copied to debug output: $debugOut" -ForegroundColor Gray
}

Write-Host ""
Write-Host "=== Build complete! ===" -ForegroundColor Green
Write-Host "DLLs copied to: $outDir" -ForegroundColor Green
Write-Host "Re-run 'dotnet build' to pick up the native binaries." -ForegroundColor Cyan
