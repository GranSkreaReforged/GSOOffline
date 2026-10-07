# Shared helpers for build.ps1 / release.ps1. Compatible with Windows PowerShell 5.1 and PowerShell 7.

$ErrorActionPreference = 'Stop'

$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:UserProps = Join-Path $script:RepoRoot 'GameDir.user.props'

# Pinned BepInEx build the plugin is developed and released against.
$script:BepInExVersion = '5.4.23.5'
$script:BepInExUrl = "https://github.com/BepInEx/BepInEx/releases/download/v$($script:BepInExVersion)/BepInEx_win_x64_$($script:BepInExVersion).zip"
$script:BepInExSha256 = '82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4'

$script:SteamAppId = '595110'
$script:GameFolderName = 'Gran Skrea Online'

function Get-RepoRoot { $script:RepoRoot }

function Test-GameDir([string]$Path) {
    $Path -and (Test-Path (Join-Path $Path 'GSO_Data\Managed\Assembly-CSharp.dll'))
}

# All Steam library roots: the Steam install itself plus every entry in libraryfolders.vdf.
function Get-SteamLibraries {
    $roots = @()
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
        $props = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
        if ($props) {
            foreach ($name in 'SteamPath', 'InstallPath') {
                if ($props.$name) { $roots += ($props.$name -replace '/', '\') }
            }
        }
    }
    $libraries = @()
    foreach ($root in ($roots | Select-Object -Unique)) {
        $libraries += $root
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $libraries += ($m.Groups[1].Value -replace '\\\\', '\')
            }
        }
    }
    $libraries | Select-Object -Unique
}

function Find-GameDir {
    foreach ($lib in Get-SteamLibraries) {
        $candidate = Join-Path $lib "steamapps\common\$($script:GameFolderName)"
        if (Test-GameDir $candidate) { return (Resolve-Path $candidate).Path }
    }
    $null
}

function Read-SavedGameDir {
    if (-not (Test-Path $script:UserProps)) { return $null }
    $m = [regex]::Match((Get-Content $script:UserProps -Raw), '<GameDir>([^<]+)</GameDir>')
    if ($m.Success) { $m.Groups[1].Value } else { $null }
}

function Save-GameDir([string]$Path) {
    $escaped = [System.Security.SecurityElement]::Escape($Path)
    @"
<!-- Machine-specific; written by build.ps1. Not committed. -->
<Project>
  <PropertyGroup>
    <GameDir>$escaped</GameDir>
  </PropertyGroup>
</Project>
"@ | Set-Content -Path $script:UserProps -Encoding UTF8
}

# Precedence mirrors Directory.Build.props: explicit > env var > saved > auto-detect.
function Resolve-GameDir([string]$Explicit) {
    $sources = @(
        @{ Name = 'parameter'; Path = $Explicit },
        @{ Name = 'GSO_GAME_DIR'; Path = $env:GSO_GAME_DIR },
        @{ Name = 'GameDir.user.props'; Path = (Read-SavedGameDir) }
    )
    foreach ($s in $sources) {
        if (-not $s.Path) { continue }
        if (Test-GameDir $s.Path) { return (Resolve-Path $s.Path).Path }
        throw "Gran Skrea Online not found at '$($s.Path)' (from $($s.Name)). Expected GSO_Data\Managed\Assembly-CSharp.dll there."
    }
    $found = Find-GameDir
    if ($found) { return $found }
    throw "Could not find Gran Skrea Online in any Steam library. Pass -GameDir 'X:\path\to\Gran Skrea Online' or set GSO_GAME_DIR."
}

function Get-BepInExZip {
    $cache = Join-Path $script:RepoRoot '.cache'
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    $zip = Join-Path $cache "BepInEx_win_x64_$($script:BepInExVersion).zip"
    if (-not (Test-Path $zip)) {
        Write-Host "Downloading BepInEx $($script:BepInExVersion)..."
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $script:BepInExUrl -OutFile "$zip.part" -UseBasicParsing
        Move-Item "$zip.part" $zip -Force
    }
    $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
    if ($hash -ne $script:BepInExSha256) {
        Remove-Item $zip -Force
        throw "BepInEx download hash mismatch (got $hash). Refusing to use it."
    }
    $zip
}

function Install-BepInEx([string]$GameDir) {
    if (Test-Path (Join-Path $GameDir 'BepInEx\core\BepInEx.dll')) {
        Write-Host "BepInEx already installed in $GameDir"
        return
    }
    $zip = Get-BepInExZip
    Write-Host "Installing BepInEx $($script:BepInExVersion) into $GameDir"
    Expand-Archive -Path $zip -DestinationPath $GameDir -Force
}

function Install-SteamAppId([string]$GameDir) {
    # Lets Steamworks initialise when GSO.exe is launched directly instead of through Steam.
    $file = Join-Path $GameDir 'steam_appid.txt'
    if (-not (Test-Path $file)) { Set-Content -Path $file -Value $script:SteamAppId -NoNewline }
}

function Get-ProjectVersion {
    $props = Join-Path $script:RepoRoot 'Directory.Build.props'
    $m = [regex]::Match((Get-Content $props -Raw), '<Version>([^<]+)</Version>')
    if (-not $m.Success) { throw "No <Version> in Directory.Build.props" }
    $m.Groups[1].Value
}

function Set-ProjectVersion([string]$Version) {
    $props = Join-Path $script:RepoRoot 'Directory.Build.props'
    $text = Get-Content $props -Raw
    $text = [regex]::Replace($text, '<Version>[^<]+</Version>', "<Version>$Version</Version>")
    [IO.File]::WriteAllText($props, $text)
}

function Invoke-Checked {
    param([string]$Exe, [string[]]$Arguments)
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Exe $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

Export-ModuleMember -Function * -Variable BepInExVersion
