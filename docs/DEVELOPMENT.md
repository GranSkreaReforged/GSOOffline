# Developer guide: build and run the offline server

The "server" is not a separate program. It is a BepInEx plugin that runs inside the game and answers the game's network messages itself. Building it means compiling one DLL against **your own copy** of Gran Skrea Online; running it means starting the game.

## 1. Prerequisites

- Windows, with Gran Skrea Online installed (Steam)
- [.NET SDK](https://dotnet.microsoft.com/download) 9.0.200 or newer
- PowerShell 7 (`pwsh`) for releases; 5.1 is fine for building
- Optional:
  - Python 3.10+ for the data tools
  - [dnSpyEx](https://github.com/dnSpyEx/dnSpy) for decompiling

## 2. Get the code

```powershell
git clone <repo-url> GSOOffline
cd GSOOffline
```

## 3. First build

```powershell
.\build.ps1 -InstallBepInEx
```

This command:
1. Finds the game in your Steam libraries (or pass `-GameDir 'X:\...\Gran Skrea Online'`).
2. Installs the pinned, hash-checked BepInEx 5.4.23.5 and `steam_appid.txt`.
3. Builds `GSOOffline.dll` and copies it to `<game>\BepInEx\plugins\GSOOffline\`.

After that, `.\build.ps1` is all you need. **Close the game before building**, because the running game locks the DLL.

## 4. Run the server (= play the game)

1. Start Gran Skrea Online, from Steam or `GSO.exe`.
2. Log in with any name; the password is ignored.
3. Create a character, then press Done.

Useful files:

| Path | What it is |
|---|---|
| `<game>\BepInEx\LogOutput.log` | Server log. Look for `Unhandled client event` (the to-do list) and errors. |
| `%USERPROFILE%\AppData\LocalLow\Gran Skrea\Gran Skrea\output_log.txt` | Unity's own log (client exceptions) |
| `<game>\OfflineSaves\` | Accounts and characters as JSON |
| `<game>\BepInEx\config\gso.offline.server.cfg` | Settings; see [COMMANDS.md](COMMANDS.md) |

To skip the menus while developing, set `AutoLogin` / `AutoCharacter` in the config, or use the DevBridge.

## 5. Develop and test

1. Read how the client behaves: `.\tools\decompile.ps1`, then [PROTOCOL.md](PROTOCOL.md).
2. Change code in `src\GSOOffline\` (the file map is in the README).
3. `.\build.ps1`
4. Test through the real client:

   ```powershell
   .\tools\dev\devbridge.ps1 -Enable
   .\tools\dev\devbridge.ps1 -Launch -Commands 'npcs 5' -Filter 'uid='
   .\tools\dev\devbridge.ps1 -Disable -ResetSaves
   ```

   See [DEVBRIDGE.md](DEVBRIDGE.md) for the test commands.
5. Update `CHANGELOG.md` under `## [Unreleased]`.

Quest glue (which NPC says what, and when) lives in `src\GSOOffline\Data\content.json`. Game data tools are in `tools\datamining\`. Everything is listed in [COMMANDS.md](COMMANDS.md).

## 6. Branches and releases

- `main` holds releases; `dev` is where work happens.
- Merge with `git switch main; git merge --ff-only dev; git switch dev`.
- Release with `.\release.ps1 -Version x.y.z` (try `-DryRun` first). The zips never contain game files.

## Rules

- Never commit game files, `decomp\` or `extracted\` (they are git-ignored).
- Target framework is .NET 3.5 (Unity 2017.4 Mono), so check that the APIs you use exist there.
- Use the plugin's `Json` class, not `UnityEngine.JsonUtility`, for the plugin's own data.
- License: GPL-3.0-or-later.
