# Installing GSO Offline Server

There are two ways to install, and both end with the same files in your game folder:

| | [From a release](#from-a-release) | [From a build](#from-a-build) |
|---|---|---|
| For | players | developers, or anyone who wants to build it themselves |
| You need | the game | the game, the .NET SDK, PowerShell |
| You get | the plugin, ready to play | the same, built from source and reviewable before installing |

Either way you need **your own copy of Gran Skrea Online** (Steam). Nothing here contains or redistributes game files.

**Finding the game folder:** in Steam, right-click Gran Skrea Online → Manage → Browse local files. It's the folder that contains `GSO.exe`, called `<game>` below.

---

## From a release

### 1. Download

From the releases page, pick **one** zip:

| Zip | Use it when |
|---|---|
| `GSOOffline-<version>-with-BepInEx.zip` | BepInEx isn't installed yet (no `BepInEx` folder in `<game>`) |
| `GSOOffline-<version>.zip` | BepInEx 5 (x64) is already installed, e.g. by GSO HD Textures |

Optional: check the download against `SHA256SUMS.txt` from the same release. In PowerShell:

```powershell
Get-FileHash .\GSOOffline-<version>.zip -Algorithm SHA256   # must match the line in SHA256SUMS.txt
```

### 2. Extract into the game folder

Close the game, then extract the zip **into `<game>`**, merging folders and replacing files if asked. Both zips are laid out like the game folder:

```
<game>\
  GSO.exe
  steam_appid.txt                    (lets GSO.exe start without going through Steam)
  winhttp.dll, doorstop_config.ini   (BepInEx loader; only in the -with-BepInEx zip)
  BepInEx\
    core\...                         (BepInEx itself; only in the -with-BepInEx zip)
    plugins\GSOOffline\
      GSOOffline.dll
      README.md, CHANGELOG.md, LICENSE, INSTALL.md (this guide)
```

### 3. Play

1. Start the game, from Steam or `GSO.exe`. The first start with BepInEx takes a little longer.
2. Log in with any username. The password is ignored.
3. Create a character and press Done.

To check it loaded: `<game>\BepInEx\LogOutput.log` contains `GSO Offline Server <version> loaded`.

- Characters and accounts are saved in `<game>\OfflineSaves\`. **Back this folder up**; it is your progress.
- Settings are in `<game>\BepInEx\config\gso.offline.server.cfg` (created on first start). `AutoLogin` and `AutoCharacter` skip the menus.
- In-game chat commands: `/help`, `/pos`, `/tele x y z`, `/scene id [x y z]`, `/wayshrine id`, `/wayshrines`, `/time 0-2400`, `/save`.

---

## From a build

### 1. Prerequisites

- Windows with your own Gran Skrea Online install.
- The [.NET SDK](https://dotnet.microsoft.com/download) 9.0.200 or newer.
- PowerShell: Windows PowerShell 5.1 is enough to build. Releases need PowerShell 7.
- Git, to get the source.

You do **not** need BepInEx in the game to build. The build downloads the pinned BepInEx 5.4.23.5 (checksum-verified) into the repo's `.cache\` and compiles against that.

### 2. Build

```powershell
git clone <repo-url> GSOOffline
cd GSOOffline
.\build.ps1                         # Debug build
.\build.ps1 -Configuration Release  # or a Release build
```

The game is found automatically in your Steam libraries (override with `-GameDir 'X:\...\Gran Skrea Online'`). The build only **reads** the game's DLLs to compile against. It never copies them into the output and never writes to the game.

### 3. Review the output

Everything the build produces is in `artifacts\build\<Configuration>\`, laid out exactly like the game folder:

```
artifacts\build\Debug\
  INSTALL.txt                               (these steps, short version)
  BepInEx\plugins\GSOOffline\GSOOffline.dll
```

That DLL is the only file that goes into the game. The server's quest and door data are embedded in it.

### 4. Install

Close the game first. Pick one of these:

**By hand**
1. If `<game>` has no `BepInEx` folder, install BepInEx 5.4.23.5 x64: download `BepInEx_win_x64_5.4.23.5.zip` from [BepInEx releases](https://github.com/BepInEx/BepInEx/releases) and extract it into `<game>`.
2. Copy the `BepInEx` folder from `artifacts\build\<Configuration>\` into `<game>`, merging.
3. Optional: create `<game>\steam_appid.txt` containing `595110`, to start `GSO.exe` without Steam.

**With the script**
```powershell
.\build.ps1 -InstallBepInEx -Deploy   # first time: installs BepInEx and steam_appid.txt into the game, then copies the plugin
.\build.ps1 -Deploy                   # afterwards: build and copy
```

`-Deploy` copies exactly the files in `artifacts\build\<Configuration>\BepInEx\` and deletes nothing.

### 5. Play

As for a release: start the game, log in with any name, and look for `GSO Offline Server <version> loaded` in `<game>\BepInEx\LogOutput.log`. For the development loop (testing, decompiling, the DevBridge), continue with [DEVELOPMENT.md](DEVELOPMENT.md).

---

## Updating

- **Release:** extract the new plugin-only zip over the old one. Saves and settings stay.
- **Build:** `git pull`, then `.\build.ps1 -Deploy`.

Back up `<game>\OfflineSaves\` before updating, and check the changelog for notes about saves.

## Uninstalling

- **Just this mod:** delete `<game>\BepInEx\plugins\GSOOffline\` and, if you like, `<game>\BepInEx\config\gso.offline.server.cfg`. Leave `<game>\OfflineSaves\` alone unless you want to lose your characters.
- **BepInEx as well** (only if no other mod needs it, e.g. GSO HD Textures): also delete `winhttp.dll`, `doorstop_config.ini`, `steam_appid.txt` and the `BepInEx` folder from `<game>`.

The game itself is never modified, so after this it is back to stock (and can't connect anywhere, since the official servers are gone).

## Troubleshooting

| Symptom | Check |
|---|---|
| No `LogOutput.log`, login fails | BepInEx isn't installed or isn't the x64 build. `winhttp.dll` must sit next to `GSO.exe`. |
| Starting `GSO.exe` directly doesn't work | Start the game from Steam, or add `steam_appid.txt` containing `595110` to `<game>` (Steam must still be running). |
| A quest NPC says nothing, or something doesn't work | Look in `LogOutput.log` for `Unhandled client event`: the feature isn't implemented yet (see the README's status list). |
| `Couldn't write ... Is the game running?` from `-Deploy` | Close the game; it locks the plugin DLL. |
| Build: `Could not find Gran Skrea Online` | Pass `-GameDir 'X:\...\Gran Skrea Online'`, or set the `GSO_GAME_DIR` environment variable. |
