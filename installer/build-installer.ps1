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

Get-ChildItem -Path $installerOutputDir -Filter "ClaudeTimer-Setup-*.exe" -ErrorAction SilentlyContinue |
    Remove-Item -Force

$isccUser = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
$isccProgramFiles = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

if (Test-Path $isccUser)
{
    $iscc = $isccUser
}
elseif (Test-Path $isccProgramFiles)
{
    $iscc = $isccProgramFiles
}
else
{
    throw "ISCC.exe blev ikke fundet. Installér Inno Setup 6 først."
}

& $DotnetPath publish .\src\ClaudeTimer\ClaudeTimer.csproj -p:PublishProfile=Folder
& $iscc .\installer\ClaudeTimer.iss

$latestInstaller = Get-ChildItem -Path $installerOutputDir -Filter "ClaudeTimer-Setup-*.exe" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $latestInstaller)
{
    throw "Ingen installer blev genereret i $installerOutputDir"
}

$hash = Get-FileHash -Algorithm SHA256 -Path $latestInstaller.FullName
$hashFilePath = "$($latestInstaller.FullName).sha256"
"$($hash.Hash.ToLowerInvariant()) *$($latestInstaller.Name)" | Set-Content -Path $hashFilePath -Encoding ASCII

$hashTxtPath = Join-Path $installerOutputDir "SHA256SUMS.txt"
"$($hash.Hash.ToLowerInvariant()) *$($latestInstaller.Name)" | Set-Content -Path $hashTxtPath -Encoding ASCII

Write-Host "Installer:" $latestInstaller.FullName
Write-Host "SHA256:" $hash.Hash
Write-Host "Hashfil:" $hashFilePath
Write-Host "Oversigt:" $hashTxtPath