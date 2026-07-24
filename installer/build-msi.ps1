param(
    [string]$DotnetPath = "C:\Program Files\dotnet\dotnet.exe"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
Set-Location $repoRoot

$installerOutputDir = Join-Path $repoRoot "artifacts\installer"
if (-not (Test-Path $installerOutputDir))
{
    New-Item -ItemType Directory -Path $installerOutputDir | Out-Null
}

Get-ChildItem -Path $installerOutputDir -Filter "ClaudeTimer-*-win-x64.msi" -ErrorAction SilentlyContinue |
    Remove-Item -Force

& $DotnetPath publish .\src\ClaudeTimer\ClaudeTimer.csproj -p:PublishProfile=Folder
& $DotnetPath build .\installer\msi\ClaudeTimer.Msi.wixproj -c Release

$latestMsi = Get-ChildItem -Path $installerOutputDir -Filter "ClaudeTimer-*-win-x64.msi" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $latestMsi)
{
    throw "Ingen MSI blev genereret i $installerOutputDir"
}

$hash = Get-FileHash -Algorithm SHA256 -Path $latestMsi.FullName
$hashLine = "$($hash.Hash.ToLowerInvariant()) *$($latestMsi.Name)"

$hashFilePath = "$($latestMsi.FullName).sha256"
$hashLine | Set-Content -Path $hashFilePath -Encoding ASCII

$hashTxtPath = Join-Path $installerOutputDir "SHA256SUMS-MSI.txt"
$hashLine | Set-Content -Path $hashTxtPath -Encoding ASCII

Write-Host "MSI:" $latestMsi.FullName
Write-Host "SHA256:" $hash.Hash
Write-Host "Hashfil:" $hashFilePath
Write-Host "Oversigt:" $hashTxtPath