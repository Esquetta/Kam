[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [string]$OutputRoot = ""
)

# Publishes the desktop app as a self-contained Windows x64 folder, removes files Windows does
# not need, and writes Kam-<version>-win-x64.zip with a SHA-256 file next to it.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$uiProject = Join-Path $repoRoot "src\Ui\SmartVoiceAgent.Ui\SmartVoiceAgent.Ui.csproj"
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\release"
}

$name = "Kam-$Version-win-x64"
$publishDir = Join-Path $OutputRoot $name
$zipPath = Join-Path $OutputRoot "$name.zip"

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

dotnet publish $uiProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishReadyToRun=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -p:AssemblyVersion="$Version.0" `
    -p:FileVersion="$Version.0" `
    --output $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# Packages copy symbols and native libraries for every platform; keep Windows x64 only.
Get-ChildItem $publishDir -Recurse -File -Filter *.pdb | Remove-Item -Force
$keep = @("win-x64", "vulkan")
Get-ChildItem (Join-Path $publishDir "runtimes") -Directory |
    Where-Object { $keep -notcontains $_.Name } |
    Remove-Item -Recurse -Force
Get-ChildItem (Join-Path $publishDir "runtimes\vulkan") -Directory |
    Where-Object { $_.Name -ne "win-x64" } |
    Remove-Item -Recurse -Force
$x86Dir = Join-Path $publishDir "x86"
if (Test-Path $x86Dir) {
    Remove-Item $x86Dir -Recurse -Force
}

# The launcher finds SmartVoiceAgent.Ui.dll by its embedded name, so it can carry the product name.
Rename-Item (Join-Path $publishDir "SmartVoiceAgent.Ui.exe") "Kam.exe"

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Compress-Archive -Path $publishDir -DestinationPath $zipPath -CompressionLevel Optimal

$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $name.zip" | Set-Content -Path "$zipPath.sha256" -Encoding ascii

$sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "Package: $zipPath ($sizeMb MB)"
Write-Host "SHA-256: $hash"
