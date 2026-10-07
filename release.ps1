<#
.SYNOPSIS
    Cut a release: bump version, build Release, package zips + checksums, commit and tag.

.DESCRIPTION
    Produces in dist\:
      GSOOffline-<ver>.zip               plugin only (for players who already have BepInEx)
      GSOOffline-<ver>-with-BepInEx.zip  extract into the game folder and play
      SHA256SUMS.txt
    Neither zip contains game files; players must own Gran Skrea Online.
    Nothing is pushed; push the commit and tag yourself when happy.

.EXAMPLE
    .\release.ps1 -Version 0.2.0
    .\release.ps1 -Version 0.2.0 -DryRun     # package only, no version bump/commit/tag
#>
#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version,
    [string]$GameDir,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'build\GSOBuild.psm1') -Force
$root = Get-RepoRoot
Set-Location $root

$tag = "v$Version"
if (-not $DryRun) {
    if (git status --porcelain) { throw 'Working tree is not clean. Commit or stash first.' }
    if (git tag --list $tag) { throw "Tag $tag already exists." }
}

$GameDir = Resolve-GameDir $GameDir
Save-GameDir $GameDir

$changelog = Join-Path $root 'CHANGELOG.md'
$previousVersion = Get-ProjectVersion
$originalChangelog = Get-Content $changelog -Raw

try {
    if (-not $DryRun) {
        Set-ProjectVersion $Version
        $date = Get-Date -Format 'yyyy-MM-dd'
        $updated = $originalChangelog -replace '(?m)^## \[Unreleased\]\s*$', "## [Unreleased]`n`n## [$Version] - $date"
        if ($updated -eq $originalChangelog) { throw 'CHANGELOG.md has no "## [Unreleased]" heading.' }
        [IO.File]::WriteAllText($changelog, $updated)
    }

    $project = Join-Path $root 'src\GSOOffline\GSOOffline.csproj'
    $out = Join-Path $root 'artifacts\release'
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    Invoke-Checked dotnet @('build', $project, '-c', 'Release', '-nologo', '-p:DeployToGame=false', "-p:Version=$Version", '-o', $out)

    $dist = Join-Path $root 'dist'
    $staging = Join-Path $root 'artifacts\staging'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    foreach ($d in $staging) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }

    # Layout mirrors the game folder so either zip can be extracted straight into it.
    $plugin = Join-Path $staging 'plugin'
    $pluginDir = Join-Path $plugin 'BepInEx\plugins\GSOOffline'
    New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
    Copy-Item (Join-Path $out 'GSOOffline.dll') $pluginDir
    Copy-Item (Join-Path $root 'README.md') (Join-Path $pluginDir 'README.md')
    Copy-Item $changelog (Join-Path $pluginDir 'CHANGELOG.md')
    Copy-Item (Join-Path $root 'LICENSE') (Join-Path $pluginDir 'LICENSE')
    Set-Content -Path (Join-Path $plugin 'steam_appid.txt') -Value '595110' -NoNewline

    $full = Join-Path $staging 'full'
    Expand-Archive -Path (Get-BepInExZip) -DestinationPath $full
    Copy-Item (Join-Path $plugin '*') $full -Recurse -Force

    $zipPlugin = Join-Path $dist "GSOOffline-$Version.zip"
    $zipFull = Join-Path $dist "GSOOffline-$Version-with-BepInEx.zip"
    foreach ($z in $zipPlugin, $zipFull) { if (Test-Path $z) { Remove-Item $z } }
    Compress-Archive -Path (Join-Path $plugin '*') -DestinationPath $zipPlugin
    Compress-Archive -Path (Join-Path $full '*') -DestinationPath $zipFull

    $sums = foreach ($z in $zipPlugin, $zipFull) {
        "{0}  {1}" -f (Get-FileHash -Algorithm SHA256 $z).Hash.ToLowerInvariant(), (Split-Path $z -Leaf)
    }
    $sums | Set-Content (Join-Path $dist 'SHA256SUMS.txt')
}
catch {
    if (-not $DryRun) {
        Set-ProjectVersion $previousVersion
        [IO.File]::WriteAllText($changelog, $originalChangelog)
    }
    throw
}

if (-not $DryRun) {
    Invoke-Checked git @('add', 'Directory.Build.props', 'CHANGELOG.md')
    Invoke-Checked git @('commit', '-m', "Release $tag")
    Invoke-Checked git @('tag', '-a', $tag, '-m', "GSO Offline Server $Version")
}

Write-Host ''
Write-Host "Packaged $Version (BepInEx $BepInExVersion):" -ForegroundColor Green
Get-Content (Join-Path $dist 'SHA256SUMS.txt') | ForEach-Object { Write-Host "  $_" }
if (-not $DryRun) {
    Write-Host "Committed and tagged $tag. Publish with: git push --follow-tags"
}
