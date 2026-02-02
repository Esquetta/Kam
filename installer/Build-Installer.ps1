#requires -Version 5.1
<#
.SYNOPSIS
    Build script for Kam AI Voice Assistant Installer

.DESCRIPTION
    This script builds the Kam application and creates an MSI installer using WiX Toolset.

.EXAMPLE
    .\Build-Installer.ps1
    Builds the installer with default settings.

.EXAMPLE
    .\Build-Installer.ps1 -Version "1.1.0"
    Builds the installer with a specific version number.
#>

[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [switch]$SkipBuild = $false
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "Continue"

# Get script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$rootDir = Split-Path -Parent $scriptDir

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Kam Installer Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Version: $Version"
Write-Host "Configuration: $Configuration"
Write-Host "Root Directory: $rootDir"
Write-Host ""

# Check prerequisites
Write-Host "Checking prerequisites..." -ForegroundColor Yellow

# Check for wix
$wix = Get-Command wix -ErrorAction SilentlyContinue
if (-not $wix) {
    Write-Host "WiX Toolset not found. Installing..." -ForegroundColor Yellow
    dotnet tool install --global wix
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to install WiX Toolset"
        exit 1
    }
}
Write-Host "WiX Toolset: $($wix.Source)" -ForegroundColor Green

# Check for dotnet
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Error ".NET SDK not found. Please install .NET 9.0 SDK."
    exit 1
}
Write-Host ".NET SDK: $($dotnet.Source)" -ForegroundColor Green

# Build application
if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "Building Kam Application..." -ForegroundColor Yellow
    Write-Host "========================================"
    
    $publishDir = Join-Path $rootDir "publish"
    
    # Clean publish directory
    if (Test-Path $publishDir) {
        Write-Host "Cleaning publish directory..."
        Remove-Item -Path $publishDir -Recurse -Force
    }
    
    # Publish the application
    $publishArgs = @(
        "publish"
        (Join-Path $rootDir "src\Ui\SmartVoiceAgent.Ui\SmartVoiceAgent.Ui.csproj")
        "-c", $Configuration
        "-r", "win-x64"
        "--self-contained", "true"
        "-p:PublishSingleFile=true"
        "-p:IncludeNativeLibrariesForSelfExtract=true"
        "-p:EnableCompressionInSingleFile=true"
        "-p:DebugType=None"
        "-p:DebugSymbols=false"
        "-o", $publishDir
    )
    
    Write-Host "Executing: dotnet $($publishArgs -join ' ')"
    & dotnet @publishArgs
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to build application"
        exit 1
    }
    
    Write-Host "Application published successfully to: $publishDir" -ForegroundColor Green
}

# Build installer
Write-Host ""
Write-Host "Building Installer..." -ForegroundColor Yellow
Write-Host "========================================"

$installerDir = Join-Path $rootDir "installer"
$outputDir = Join-Path $rootDir "artifacts"

# Create output directory
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

# Build MSI using WiX
$wixArgs = @(
    "build"
    (Join-Path $installerDir "Product.wxs")
    "-o", (Join-Path $outputDir "Kam-$Version-x64.msi")
    "-ext", "WixToolset.UI.wixext"
    "-ext", "WixToolset.Util.wixext"
    "-d", "Version=$Version"
    "-d", "PublishDir=$(Join-Path $rootDir 'publish')"
)

Write-Host "Executing: wix $($wixArgs -join ' ')"
Push-Location $installerDir
try {
    & wix @wixArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to build installer"
        exit 1
    }
}
finally {
    Pop-Location
}

# Verify output
$msiPath = Join-Path $outputDir "Kam-$Version-x64.msi"
if (Test-Path $msiPath) {
    $fileInfo = Get-Item $msiPath
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "Installer built successfully!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "Output: $msiPath"
    Write-Host "Size: $([math]::Round($fileInfo.Length / 1MB, 2)) MB"
    Write-Host ""
    Write-Host "Installation command:"
    Write-Host "  msiexec /i `"$msiPath`" /quiet /norestart"
}
else {
    Write-Error "Installer file not found at expected location: $msiPath"
    exit 1
}

Write-Host ""
Write-Host "Build completed successfully!" -ForegroundColor Green
