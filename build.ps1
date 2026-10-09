<#
.SYNOPSIS
    Build GSOOffline into artifacts\build\<Configuration>\, laid out like the game folder. Deploying is optional.

.DESCRIPTION
    The build only reads your Gran Skrea Online install (its DLLs to compile against) and never writes
    to it. The result is artifacts\build\<Configuration>\BepInEx\plugins\GSOOffline\GSOOffline.dll plus an
    INSTALL.txt saying where it goes, so you can review it and install it by hand.
    -Deploy and -InstallBepInEx are conveniences that copy into the game for you.

.EXAMPLE
    .\build.ps1                           # Debug build into artifacts\build\Debug; the game is untouched
    .\build.ps1 -Deploy                   # ...and copy it into the game (close the game first)
    .\build.ps1 -Configuration Release
    .\build.ps1 -InstallBepInEx -Deploy   # first time: also put BepInEx and steam_appid.txt in the game
    .\build.ps1 -GameDir 'E:\Games\Gran Skrea Online'   # remembered in GameDir.user.props
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$GameDir,
    [switch]$Deploy,
    [switch]$InstallBepInEx,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'build\GSOBuild.psm1') -Force

$GameDir = Resolve-GameDir $GameDir
Save-GameDir $GameDir
Write-Host "Game (read for compiling only): $GameDir"
Get-BepInExCore | Out-Null   # BepInEx's DLLs for compiling, from the pinned zip in .cache

$project = Join-Path $PSScriptRoot 'src\GSOOffline\GSOOffline.csproj'
$stage = Get-StageDir $Configuration
if ($Clean) {
    Invoke-Checked dotnet @('clean', $project, '-c', $Configuration, '-nologo', '-v', 'q')
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
}
$label = Get-DevBuildLabel   # dev build: stamped with branch and commit (release builds come from release.ps1)
Invoke-Checked dotnet @('build', $project, '-c', $Configuration, '-nologo', "-p:DevBuildLabel=$label")
Write-InstallNote $stage 'GSOOffline' @'
  Optional: to start GSO.exe directly instead of through Steam, create steam_appid.txt
     containing 595110 in the game folder.
'@

if ($InstallBepInEx) {
    Install-BepInEx $GameDir
    Install-SteamAppId $GameDir
}
if ($Deploy) {
    Deploy-Stage $stage $GameDir
    Write-Host "Deployed into $GameDir" -ForegroundColor Green
}
else {
    Write-Host "Build output: $stage (see INSTALL.txt). The game was not changed; add -Deploy to copy it in." -ForegroundColor Green
}
