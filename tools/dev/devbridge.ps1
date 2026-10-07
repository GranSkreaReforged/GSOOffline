<#
.SYNOPSIS
    Drive the game through the plugin's DevBridge and print the resulting log lines.

.DESCRIPTION
    The DevBridge (src/GSOOffline/DevBridge.cs) polls a command file and runs each line in-game.
    This script writes those lines for you, waits between them, and returns the BepInEx log output
    they produced. See docs/DEVBRIDGE.md for the command reference.

.EXAMPLE
    .\tools\dev\devbridge.ps1 -Enable -Account Tester -Character Testguy   # turn on dev config + auto-login
    .\tools\dev\devbridge.ps1 -Launch -Commands 'npcs 5' -Filter 'uid='
    .\tools\dev\devbridge.ps1 -Commands 'client interactNpc 10018 $me','client sendDialogueOptionChoise $me 1' -Filter dialogue
    .\tools\dev\devbridge.ps1 -Disable -ResetSaves                         # back to normal play
#>
[CmdletBinding()]
param(
    [string]$GameDir,
    [switch]$Enable,
    [string]$Account = 'Tester',
    [string]$Character = 'Testguy',
    [switch]$Disable,
    [switch]$ResetSaves,
    [switch]$Launch,
    [string[]]$Commands = @(),
    [double]$Wait = 2,
    [string]$Filter = ''
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\build\GSOBuild.psm1') -Force
$GameDir = Resolve-GameDir $GameDir
$log = Join-Path $GameDir 'BepInEx\LogOutput.log'
$cfg = Join-Path $GameDir 'BepInEx\config\gso.offline.server.cfg'
$cmdFile = Join-Path $GameDir 'OfflineSaves\dev\cmd.txt'

function Set-ConfigValue([string]$Key, [string]$Value) {
    if (-not (Test-Path $cfg)) { throw "No config at $cfg; launch the game once with the plugin installed." }
    $lines = Get-Content $cfg
    if (-not ($lines -match "^$Key\s*=")) { throw "Config key '$Key' not found in $cfg (start the game once to generate it)." }
    ($lines -replace "^$Key\s*=.*", "$Key = $Value") | Set-Content $cfg
}

if ($Enable) {
    Set-ConfigValue 'AutoLogin' $Account
    Set-ConfigValue 'AutoCharacter' $Character
    Set-ConfigValue 'DevCommandFile' 'OfflineSaves/dev/cmd.txt'
    Write-Host "DevBridge enabled (auto-login $Account / $Character)."
}
if ($Disable) {
    Get-Process GSO -ErrorAction SilentlyContinue | Stop-Process -Force
    Set-ConfigValue 'AutoLogin' ''
    Set-ConfigValue 'AutoCharacter' ''
    Set-ConfigValue 'DevCommandFile' ''
    Write-Host 'DevBridge disabled; normal login restored.'
}
if ($ResetSaves) {
    Get-Process GSO -ErrorAction SilentlyContinue | Stop-Process -Force
    foreach ($d in 'accounts', 'characters', 'dev') {
        $p = Join-Path $GameDir "OfflineSaves\$d"
        if (Test-Path $p) { Remove-Item $p -Recurse -Force }
    }
    Write-Host 'Deleted OfflineSaves accounts, characters and dev output.'
}

if ($Launch) {
    Get-Process GSO -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep 1
    # Remove the old log first, or the wait below would match the previous run.
    Remove-Item $log -ErrorAction SilentlyContinue
    Start-Process -FilePath (Join-Path $GameDir 'GSO.exe') -WorkingDirectory $GameDir
    $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $deadline) {
        if ((Test-Path $log) -and (Select-String -Path $log -Pattern 'harvestables\.' -Quiet)) { break }
        Start-Sleep 2
    }
    if (-not ((Test-Path $log) -and (Select-String -Path $log -Pattern 'harvestables\.' -Quiet))) {
        throw 'The world did not load within 120 s (is auto-login enabled? see -Enable).'
    }
    Start-Sleep 3
}

if ($Commands.Count -eq 0 -and -not $Launch) { return }

$before = if ($Launch -or -not (Test-Path $log)) { 0 } else { (Get-Content $log).Count }
New-Item -ItemType Directory -Force (Split-Path $cmdFile) | Out-Null
foreach ($c in $Commands) {
    # The bridge deletes the file once it has read it; wait so commands are not overwritten.
    $until = (Get-Date).AddSeconds(10)
    while ((Test-Path $cmdFile) -and (Get-Date) -lt $until) { Start-Sleep -Milliseconds 200 }
    Set-Content $cmdFile $c
    Start-Sleep -Seconds $Wait
}

$lines = Get-Content $log | Select-Object -Skip $before
if ($Filter) { $lines = $lines | Where-Object { $_ -match $Filter } }
$lines
