<#
.SYNOPSIS
    Decompile the game's scripts to decomp\ (git-ignored) for reference while implementing server logic.
    Uses dnSpy.Console.exe from dnSpyEx (https://github.com/dnSpyEx/dnSpy).
#>
[CmdletBinding()]
param(
    [string]$GameDir,
    [string]$DnSpyConsole
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\build\GSOBuild.psm1') -Force
$GameDir = Resolve-GameDir $GameDir
$managed = Join-Path $GameDir 'GSO_Data\Managed'

if (-not $DnSpyConsole) {
    $DnSpyConsole = @((Join-Path $managed 'dnSpy.Console.exe'), (Get-Command dnSpy.Console.exe -ErrorAction SilentlyContinue).Source) |
        Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}
if (-not $DnSpyConsole) { throw 'dnSpy.Console.exe not found. Pass -DnSpyConsole <path>.' }

$out = Join-Path (Get-RepoRoot) 'decomp'
$assemblies = 'Assembly-CSharp', 'Assembly-CSharp-firstpass', 'Assembly-UnityScript', 'Assembly-UnityScript-firstpass' |
    ForEach-Object { Join-Path $managed "$_.dll" }
Invoke-Checked $DnSpyConsole (@('--no-sln', '-o', $out) + $assemblies)
Write-Host "Decompiled to $out"
