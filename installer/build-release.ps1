param(
    [string]$DotnetPath = "C:\Program Files\dotnet\dotnet.exe"
)

# Bygger alle release-filer (offline/self-contained) og samlede SHA256-checksums:
#   ClaudeTimer-Setup-<v>.exe, ClaudeTimer-<v>-win-x64.msi,
#   ClaudeTimer-<v>-win-x64-allusers.msi, ClaudeTimer-<v>-win-x64.exe,
#   ClaudeTimer-<v>-win-x64.zip, *.sha256 og SHA256SUMS.txt
# Resultatet lægges i artifacts\release-v<v>.

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
Set-Location $repoRoot

[xml]$csproj = Get-Content .\src\ClaudeTimer\ClaudeTimer.csproj
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw "Version blev ikke fundet i ClaudeTimer.csproj" }

$releaseDir = Join-Path $repoRoot "artifacts\release-v$version"
if (Test-Path $releaseDir) { Remove-Item $releaseDir -Recurse -Force }
New-Item -ItemType Directory -Path $releaseDir | Out-Null

# Ryd publish-mapperne, så der ikke følger gamle filer med i installerne.
foreach ($dir in "artifacts\folder", "artifacts\single-file")
{
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
}

& $DotnetPath test .\ClaudeTimer.slnx -nologo
if ($LASTEXITCODE -ne 0) { throw "Tests fejlede" }

& "$PSScriptRoot\build-installer.ps1" -DotnetPath $DotnetPath
& "$PSScriptRoot\build-msi.ps1" -DotnetPath $DotnetPath
& "$PSScriptRoot\build-msi-allusers.ps1" -DotnetPath $DotnetPath

& $DotnetPath publish .\src\ClaudeTimer\ClaudeTimer.csproj -p:PublishProfile=SingleFile
if ($LASTEXITCODE -ne 0) { throw "Single-file publish fejlede" }

$installerDir = Join-Path $repoRoot "artifacts\installer"
$files = @(
    (Join-Path $installerDir "ClaudeTimer-Setup-$version.exe"),
    (Join-Path $installerDir "ClaudeTimer-$version-win-x64.msi"),
    (Join-Path $installerDir "ClaudeTimer-$version-win-x64-allusers.msi")
)
foreach ($file in $files)
{
    if (-not (Test-Path $file)) { throw "Mangler $file" }
    Copy-Item $file $releaseDir
}

Copy-Item .\artifacts\single-file\ClaudeTimer.exe (Join-Path $releaseDir "ClaudeTimer-$version-win-x64.exe")
Compress-Archive -Path .\artifacts\folder\* -DestinationPath (Join-Path $releaseDir "ClaudeTimer-$version-win-x64.zip")

$sumLines = foreach ($file in Get-ChildItem $releaseDir -File | Sort-Object Name)
{
    $hash = (Get-FileHash -Algorithm SHA256 -Path $file.FullName).Hash.ToLowerInvariant()
    $line = "$hash *$($file.Name)"
    $line | Set-Content -Path "$($file.FullName).sha256" -Encoding ASCII
    $line
}
$sumLines | Set-Content -Path (Join-Path $releaseDir "SHA256SUMS.txt") -Encoding ASCII

Write-Host ""
Write-Host "Release $version klar i $releaseDir"
$sumLines | ForEach-Object { Write-Host "  $_" }
