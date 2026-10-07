<#
.SYNOPSIS
    Build GSOOffline and (by default) deploy it into your Gran Skrea Online install.

.EXAMPLE
    .\build.ps1                      # Debug build, auto-detect game, deploy
    .\build.ps1 -InstallBepInEx      # first-time setup: also install BepInEx + steam_appid.txt
    .\build.ps1 -Configuration Release -NoDeploy
    .\build.ps1 -GameDir 'E:\Games\Gran Skrea Online'   # remembered in GameDir.user.props
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$GameDir,
    [switch]$NoDeploy,
    [switch]$InstallBepInEx,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'build\GSOBuild.psm1') -Force

$GameDir = Resolve-GameDir $GameDir
Save-GameDir $GameDir
Write-Host "Game: $GameDir"

if ($InstallBepInEx) {
    Install-BepInEx $GameDir
    Install-SteamAppId $GameDir
}

$project = Join-Path $PSScriptRoot 'src\GSOOffline\GSOOffline.csproj'
$deploy = if ($NoDeploy) { 'false' } else { 'true' }

if ($Clean) {
    Invoke-Checked dotnet @('clean', $project, '-c', $Configuration, '-nologo', '-v', 'q')
}
Invoke-Checked dotnet @('build', $project, '-c', $Configuration, '-nologo', "-p:DeployToGame=$deploy")
