<#
.SYNOPSIS
    Extract the data the offline server is built from, out of YOUR game install, into extracted\ (git-ignored).

    extracted\textassets\*.txt   Resources TextAssets (XMLs/NPCInfo, Dialogue, Quests, items, ...)
    extracted\markers.json       Scr_NPCDummy / Scr_HarvestableDummy / Scr_Interactable placements per scene
    extracted\scenes.txt         build index -> scene name

    Game data is proprietary: never commit anything under extracted\.
#>
[CmdletBinding()]
param([string]$GameDir)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\build\GSOBuild.psm1') -Force
$GameDir = Resolve-GameDir $GameDir
$data = Join-Path $GameDir 'GSO_Data'
$out = Join-Path (Get-RepoRoot) 'extracted'
New-Item -ItemType Directory -Force -Path (Join-Path $out 'textassets') | Out-Null

$venv = Join-Path $PSScriptRoot '.venv'
$py = Join-Path $venv 'Scripts\python.exe'
if (-not (Test-Path $py)) {
    Invoke-Checked python @('-m', 'venv', $venv)
    Invoke-Checked $py @('-m', 'pip', 'install', '-q', '-r', (Join-Path $PSScriptRoot 'requirements.txt'))
}

Invoke-Checked $py @('-I', (Join-Path $PSScriptRoot 'dump_text.py'), (Join-Path $data 'resources.assets'), (Join-Path $out 'textassets'))
Invoke-Checked $py @('-I', (Join-Path $PSScriptRoot 'dump_markers.py'), $data, (Join-Path $out 'markers.json'))
& $py -I (Join-Path $PSScriptRoot 'scenes.py') (Join-Path $data 'globalgamemanagers') | Set-Content (Join-Path $out 'scenes.txt')
Write-Host "Extracted to $out"
