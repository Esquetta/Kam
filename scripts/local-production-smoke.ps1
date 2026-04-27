[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests,
    [switch]$SkipPublish,
    [switch]$RequireAiConfig,
    [switch]$Launch,
    [switch]$PlanOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$solution = Join-Path $repoRoot "Kam.sln"
$uiProject = Join-Path $repoRoot "src\Ui\SmartVoiceAgent.Ui\SmartVoiceAgent.Ui.csproj"
$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$artifactRoot = Join-Path $repoRoot "artifacts\local-production-smoke\$runId"
$publishDir = Join-Path $artifactRoot "publish"
$summaryPath = Join-Path $artifactRoot "summary.md"
$summary = New-Object System.Collections.Generic.List[string]

function Add-SummaryLine {
    param([string]$Line)
    $summary.Add($Line) | Out-Null
}

function Invoke-SmokeStep {
    param(
        [string]$Name,
        [string[]]$Command
    )

    $commandText = $Command -join " "
    Write-Host "==> $Name" -ForegroundColor Cyan
    Add-SummaryLine "- $Name`: ``$commandText``"

    if ($PlanOnly) {
        return
    }

    $exe = $Command[0]
    $arguments = @()
    if ($Command.Count -gt 1) {
        $arguments = $Command[1..($Command.Count - 1)]
    }

    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    & $exe @arguments
    $exitCode = if ($LASTEXITCODE -is [int]) { $LASTEXITCODE } else { 0 }
    $timer.Stop()

    Add-SummaryLine "  - duration: $($timer.Elapsed.TotalSeconds.ToString('0.0'))s"
    if ($exitCode -ne 0) {
        throw "$Name failed with exit code $exitCode."
    }
}

function Get-UserSecretKeys {
    if ($PlanOnly) {
        return @()
    }

    $output = & dotnet user-secrets list --project $uiProject 2>&1
    $exitCode = if ($LASTEXITCODE -is [int]) { $LASTEXITCODE } else { 0 }
    if ($exitCode -ne 0) {
        Add-SummaryLine "- AI config: user-secrets check failed, environment variables will still be checked."
        return @()
    }

    return @($output | ForEach-Object {
        $line = $_.ToString()
        if ($line.Contains("=")) {
            $line.Split("=", 2)[0].Trim()
        }
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Test-AiConfiguration {
    $requiredKeys = @(
        "AIService:ApiKey",
        "AIService:ModelId",
        "AIService:Endpoint"
    )
    $secretKeys = @(Get-UserSecretKeys)
    $missing = New-Object System.Collections.Generic.List[string]

    foreach ($key in $requiredKeys) {
        $envKey = $key.Replace(":", "__")
        $hasSecret = $secretKeys -contains $key
        $hasEnv = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($envKey))
        if (-not ($hasSecret -or $hasEnv)) {
            $missing.Add($key) | Out-Null
        }
    }

    if ($missing.Count -eq 0) {
        Add-SummaryLine "- AI config: required planner keys are present in user-secrets or environment."
        Write-Host "==> AI config keys present" -ForegroundColor Green
        return
    }

    $message = "Missing AI config keys: $($missing -join ', ')"
    Add-SummaryLine "- AI config: $message"
    if ($RequireAiConfig) {
        throw $message
    }

    Write-Host "==> AI config warning: $message" -ForegroundColor Yellow
}

if (-not (Test-Path $solution)) {
    throw "Kam.sln was not found. Run this script from the repository checkout."
}

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
Add-SummaryLine "# Local Production Smoke"
Add-SummaryLine ""
Add-SummaryLine "- timestamp: $(Get-Date -Format o)"
Add-SummaryLine "- configuration: $Configuration"
Add-SummaryLine "- runtime: $Runtime"
Add-SummaryLine "- planOnly: $PlanOnly"
Add-SummaryLine ""
Add-SummaryLine "## Steps"

Invoke-SmokeStep "dotnet info" @("dotnet", "--info")
Invoke-SmokeStep "restore" @("dotnet", "restore", $solution)
Invoke-SmokeStep "build" @("dotnet", "build", $solution, "--configuration", $Configuration, "--no-restore")

if (-not $SkipTests) {
    Invoke-SmokeStep "tests" @("dotnet", "test", (Join-Path $repoRoot "tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj"), "--configuration", $Configuration, "--no-build")
}
else {
    Add-SummaryLine "- tests: skipped"
}

Test-AiConfiguration

if (-not $SkipPublish) {
    Invoke-SmokeStep "publish" @("dotnet", "publish", $uiProject, "--configuration", $Configuration, "--runtime", $Runtime, "--self-contained", "false", "--output", $publishDir)
    Add-SummaryLine "- publishDir: $publishDir"
}
else {
    Add-SummaryLine "- publish: skipped"
}

if ($Launch) {
    if ($SkipPublish) {
        throw "-Launch requires publish output. Remove -SkipPublish."
    }

    $exePath = Join-Path $publishDir "SmartVoiceAgent.Ui.exe"
    if (-not (Test-Path $exePath)) {
        throw "Published application executable was not found: $exePath"
    }

    if (-not $PlanOnly) {
        $process = Start-Process -FilePath $exePath -WorkingDirectory $publishDir -PassThru
        Add-SummaryLine "- launch: started process $($process.Id)"
        Write-Host "==> launched $exePath (pid $($process.Id))" -ForegroundColor Green
    }
}

Add-SummaryLine ""
Add-SummaryLine "## Result"
Add-SummaryLine "- status: completed"

if (-not $PlanOnly) {
    $summary | Set-Content -Path $summaryPath -Encoding UTF8
    Write-Host "Smoke summary: $summaryPath" -ForegroundColor Green
}
else {
    $summary | ForEach-Object { Write-Host $_ }
}
