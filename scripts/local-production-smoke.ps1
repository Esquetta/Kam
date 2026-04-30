[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests,
    [switch]$SkipPublish,
    [switch]$RequireAiConfig,
    [switch]$Launch,
    [switch]$PlanOnly,
    [switch]$SelfTestWarningParser,
    [int]$MaxBuildWarnings = 30
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
$uiSettingsPath = Join-Path $env:LOCALAPPDATA "SmartVoiceAgent\settings.json"
$summary = New-Object System.Collections.Generic.List[string]

if ($MaxBuildWarnings -lt 0) {
    throw "-MaxBuildWarnings must be zero or greater."
}

function Add-SummaryLine {
    param([string]$Line)
    $summary.Add($Line) | Out-Null
}

function Get-SmokeBuildWarningCount {
    param([object[]]$Output)

    $summaryCounts = @($Output | ForEach-Object {
        $line = $_.ToString()
        $match = [regex]::Match($line, "^\s*(\d+)\s+Warning\(s\)\s*$")
        if ($match.Success) {
            [int]$match.Groups[1].Value
        }
    })

    if ($summaryCounts.Count -gt 0) {
        return [int]$summaryCounts[-1]
    }

    return @($Output | Where-Object { $_.ToString() -match ": warning " }).Count
}

function Test-SmokeWarningParser {
    $duplicatedBuildOutput = @(
        "D:\repo\Example.cs(10,20): warning CS8602: first pass [D:\repo\Example.csproj]",
        "D:\repo\Example.cs(10,20): warning CS8602: summary pass [D:\repo\Example.csproj]",
        "    1 Warning(s)",
        "    0 Error(s)"
    )

    $summaryCount = Get-SmokeBuildWarningCount -Output $duplicatedBuildOutput
    if ($summaryCount -ne 1) {
        throw "Expected MSBuild summary warning count 1, got $summaryCount."
    }

    $streamOnlyOutput = @(
        "D:\repo\One.cs(1,1): warning CS0001: first [D:\repo\One.csproj]",
        "D:\repo\Two.cs(2,2): warning CS0002: second [D:\repo\Two.csproj]"
    )

    $streamCount = Get-SmokeBuildWarningCount -Output $streamOnlyOutput
    if ($streamCount -ne 2) {
        throw "Expected stream warning count 2 when no MSBuild summary exists, got $streamCount."
    }

    Write-Host "Warning parser self-test passed" -ForegroundColor Green
}

function Invoke-SmokeStep {
    param(
        [string]$Name,
        [string[]]$Command,
        [int]$MaxWarnings = -1
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
    $output = & $exe @arguments 2>&1
    $exitCode = if ($LASTEXITCODE -is [int]) { $LASTEXITCODE } else { 0 }
    $timer.Stop()

    foreach ($line in $output) {
        Write-Host $line
    }

    Add-SummaryLine "  - duration: $($timer.Elapsed.TotalSeconds.ToString('0.0'))s"

    if ($MaxWarnings -ge 0) {
        $warningCount = Get-SmokeBuildWarningCount -Output $output
        Add-SummaryLine "  - warnings: $warningCount"
        Add-SummaryLine "  - maxWarnings: $MaxWarnings"

        if ($warningCount -gt $MaxWarnings) {
            throw "$Name produced $warningCount warnings, exceeding threshold $MaxWarnings."
        }
    }

    if ($exitCode -ne 0) {
        throw "$Name failed with exit code $exitCode."
    }
}

function Get-UserSecretKeys {
    if ($PlanOnly) {
        return @(Get-UserSecretKeysFromFile)
    }

    $output = & dotnet user-secrets list --project $uiProject 2>&1
    $exitCode = if ($LASTEXITCODE -is [int]) { $LASTEXITCODE } else { 0 }
    if ($exitCode -ne 0) {
        Add-SummaryLine "- AI config: user-secrets check failed, environment variables will still be checked."
        return @(Get-UserSecretKeysFromFile)
    }

    return @($output | ForEach-Object {
        $line = $_.ToString()
        if ($line.Contains("=")) {
            $line.Split("=", 2)[0].Trim()
        }
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-UserSecretKeysFromFile {
    try {
        [xml]$projectXml = Get-Content -Path $uiProject
        $ids = @($projectXml.Project.PropertyGroup |
            ForEach-Object { $_.UserSecretsId } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

        if ($ids.Count -eq 0) {
            return @()
        }

        $secretsPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\$($ids[0])\secrets.json"
        if (-not (Test-Path $secretsPath)) {
            return @()
        }

        $keys = New-Object System.Collections.Generic.List[string]
        $json = Get-Content -Raw -Path $secretsPath | ConvertFrom-Json
        Add-JsonConfigurationKeys -Node $json -Prefix "" -Keys $keys
        return @($keys)
    }
    catch {
        return @()
    }
}

function Add-JsonConfigurationKeys {
    param(
        [object]$Node,
        [string]$Prefix,
        [System.Collections.Generic.List[string]]$Keys
    )

    if ($null -eq $Node) {
        return
    }

    if ($Node -is [System.Management.Automation.PSCustomObject]) {
        foreach ($property in $Node.PSObject.Properties) {
            $key = if ([string]::IsNullOrWhiteSpace($Prefix)) {
                $property.Name
            }
            else {
                "${Prefix}:$($property.Name)"
            }

            if ($property.Value -is [System.Management.Automation.PSCustomObject]) {
                Add-JsonConfigurationKeys -Node $property.Value -Prefix $key -Keys $Keys
            }
            else {
                $Keys.Add($key) | Out-Null
            }
        }
    }
}

function Get-PropertyValue {
    param(
        [object]$Object,
        [string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties |
        Where-Object { $_.Name -eq $Name } |
        Select-Object -First 1

    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Test-ConfigurationKeyPresent {
    param(
        [string[]]$Aliases,
        [string[]]$SecretKeys
    )

    foreach ($alias in $Aliases) {
        if ($SecretKeys -contains $alias) {
            return $true
        }

        $envKey = $alias.Replace(":", "__")
        if (-not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($envKey))) {
            return $true
        }
    }

    return $false
}

function Test-ProfileHasPlannerRole {
    param([object]$Profile)

    $roles = @(Get-PropertyValue -Object $Profile -Name "Roles")
    foreach ($role in $roles) {
        if ($null -eq $role) {
            continue
        }

        $roleText = $role.ToString()
        if ($roleText.Equals("Planner", [StringComparison]::OrdinalIgnoreCase) -or $roleText -eq "0") {
            return $true
        }
    }

    return $false
}

function Test-ProfileEnabled {
    param([object]$Profile)

    $enabled = Get-PropertyValue -Object $Profile -Name "Enabled"
    if ($enabled -is [bool]) {
        return $enabled
    }

    return $null -ne $enabled -and $enabled.ToString().Equals("true", [StringComparison]::OrdinalIgnoreCase)
}

function Test-OllamaProvider {
    param([object]$Provider)

    if ($null -eq $Provider) {
        return $false
    }

    $providerText = $Provider.ToString()
    return $providerText.Equals("Ollama", [StringComparison]::OrdinalIgnoreCase) -or $providerText -eq "2"
}

function Test-UiPlannerProfileConfiguration {
    if (-not (Test-Path $uiSettingsPath)) {
        return $false
    }

    try {
        $settings = Get-Content -Raw -Path $uiSettingsPath | ConvertFrom-Json
        $profiles = @(Get-PropertyValue -Object $settings -Name "ModelProviderProfiles")
        if ($profiles.Count -eq 0) {
            return $false
        }

        $activePlannerProfileId = Get-PropertyValue -Object $settings -Name "ActivePlannerProfileId"
        $profile = $null
        if (-not [string]::IsNullOrWhiteSpace($activePlannerProfileId)) {
            $profile = $profiles |
                Where-Object {
                    $id = Get-PropertyValue -Object $_ -Name "Id"
                    $null -ne $id -and $id.ToString().Equals($activePlannerProfileId.ToString(), [StringComparison]::OrdinalIgnoreCase)
                } |
                Select-Object -First 1
        }

        if ($null -eq $profile) {
            $profile = $profiles |
                Where-Object { Test-ProfileHasPlannerRole -Profile $_ } |
                Select-Object -First 1
        }

        if ($null -eq $profile -or -not (Test-ProfileEnabled -Profile $profile)) {
            return $false
        }

        $provider = Get-PropertyValue -Object $profile -Name "Provider"
        $endpoint = Get-PropertyValue -Object $profile -Name "Endpoint"
        $apiKey = Get-PropertyValue -Object $profile -Name "ApiKey"
        $modelId = Get-PropertyValue -Object $profile -Name "ModelId"
        $hasApiKey = (Test-OllamaProvider -Provider $provider) -or -not [string]::IsNullOrWhiteSpace($apiKey)

        return $hasApiKey `
            -and -not [string]::IsNullOrWhiteSpace($modelId) `
            -and -not [string]::IsNullOrWhiteSpace($endpoint)
    }
    catch {
        Add-SummaryLine "- AI config: UI settings file could not be parsed."
        return $false
    }
}

function Test-AiConfiguration {
    $requiredKeys = @(
        @{ Name = "AIService:ApiKey"; Aliases = @("AIService:ApiKey") },
        @{ Name = "AIService:ModelId"; Aliases = @("AIService:ModelId") },
        @{ Name = "AIService:Endpoint"; Aliases = @("AIService:Endpoint", "AIService:EndPoint") }
    )
    $secretKeys = @(Get-UserSecretKeys)
    $missing = New-Object System.Collections.Generic.List[string]

    foreach ($key in $requiredKeys) {
        if (-not (Test-ConfigurationKeyPresent -Aliases $key.Aliases -SecretKeys $secretKeys)) {
            $missing.Add($key.Name) | Out-Null
        }
    }

    if ($missing.Count -eq 0) {
        Add-SummaryLine "- AI config: required planner keys are present in user-secrets or environment."
        Write-Host "==> AI config keys present" -ForegroundColor Green
        return
    }

    if (Test-UiPlannerProfileConfiguration) {
        Add-SummaryLine "- AI config: enabled planner profile is present in UI settings."
        Write-Host "==> AI config planner profile present in UI settings" -ForegroundColor Green
        return
    }

    $message = "Missing AI config keys: $($missing -join ', ')"
    Add-SummaryLine "- AI config: $message"
    if ($RequireAiConfig) {
        throw $message
    }

    Write-Host "==> AI config warning: $message" -ForegroundColor Yellow
}

if ($SelfTestWarningParser) {
    Test-SmokeWarningParser
    return
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
Add-SummaryLine "- maxBuildWarnings: $MaxBuildWarnings"
Add-SummaryLine ""
Add-SummaryLine "## Steps"

Invoke-SmokeStep "dotnet info" @("dotnet", "--info")
Invoke-SmokeStep "restore" @("dotnet", "restore", $solution)
Invoke-SmokeStep "build" @("dotnet", "build", $solution, "--configuration", $Configuration, "--no-restore", "--no-incremental") -MaxWarnings $MaxBuildWarnings

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
