# Command reference

Every command available for developing and running the GSO Offline Server. Run the scripts from the repo root, in PowerShell. Each script finds the game automatically (Steam libraries); `-GameDir` overrides that.

## build.ps1

Builds the plugin, and by default copies it into the game.

| Parameter | Default | Meaning |
|---|---|---|
| `-Configuration Debug\|Release` | Debug | Build configuration |
| `-GameDir <path>` | auto | Game folder (remembered in `GameDir.user.props`) |
| `-NoDeploy` | off | Don't copy the DLL into `<game>\BepInEx\plugins\GSOOffline` |
| `-InstallBepInEx` | off | Install the pinned BepInEx 5.4.23.5 (hash-checked) and `steam_appid.txt` |
| `-Clean` | off | `dotnet clean` first |

## release.ps1 (PowerShell 7)

| Parameter | Meaning |
|---|---|
| `-Version x.y.z` | Required. Bumps `Directory.Build.props`, dates `## [Unreleased]` in the CHANGELOG, commits, and tags `vX`. |
| `-DryRun` | Build and package into `dist\` only; no version bump, commit or tag |
| `-GameDir <path>` | Game folder override |

Output in `dist\`:
- `GSOOffline-x.y.z.zip`
- `GSOOffline-x.y.z-with-BepInEx.zip`
- `SHA256SUMS.txt`

It never pushes.

## tools/dev/devbridge.ps1

Drives the real client for testing. See [DEVBRIDGE.md](DEVBRIDGE.md).

| Parameter | Default | Meaning |
|---|---|---|
| `-Enable` | | Set AutoLogin, AutoCharacter and DevCommandFile in the plugin config |
| `-Account <name>` / `-Character <name>` | Tester / Testguy | Used with `-Enable` |
| `-Disable` | | Kill the game and clear those three settings |
| `-ResetSaves` | | Kill the game and delete `OfflineSaves\accounts`, `characters` and `dev` |
| `-Launch` | | Kill the game, delete the old log, start the game, and wait (up to 120 s) for the world to load |
| `-Commands <lines>` | | Bridge commands to send, one at a time |
| `-Wait <seconds>` | 2 | Pause after each command |
| `-Filter <regex>` | | Only print matching log lines |

## tools/decompile.ps1

| Parameter | Meaning |
|---|---|
| `-DnSpyConsole <path>` | dnSpyEx console. Defaults to `<game>\GSO_Data\Managed\dnSpy.Console.exe` or one on PATH. |

Writes `decomp\` (git-ignored; never commit it).

## tools/datamining

| Command | Meaning |
|---|---|
| `.\tools\datamining\extract.ps1 [-GameDir]` | Creates the venv (UnityPy) and writes `extracted\textassets\*.txt`, `extracted\markers.json` and `extracted\scenes.txt` |
| `tools\datamining\.venv\Scripts\python.exe -I tools\datamining\gen_doors.py extracted\markers.json src\GSOOffline\Data\doors.json` | Regenerate the door table |
| `dump_text.py`, `dump_markers.py`, `scenes.py`, `count_dummies.py` | Building blocks used by extract.ps1, runnable on their own (see each file's docstring/argv) |

## DevBridge commands (written to `OfflineSaves\dev\cmd.txt`)

| Command | Meaning |
|---|---|
| `client <Scr_RPCSender method> [args]` | Call a client send method as the UI would. `$me` = player name; `_` in strings becomes a space. |
| `creationdone` | Press Done in the character creator |
| `inv` | Dump inventory, equipment, silver and HP |
| `npcs [n]` / `harvestables [n]` | List the nearest NPCs or nodes, with uids |
| `near <uid>` | Step next to an NPC or node |
| `goto x y z` | Move the player (client side) |
| `shot <name>` | Screenshot to `OfflineSaves\dev\<name>.png` |
| `checkrecipes` | Verify server recipe ids against the client's |

## In-game chat commands

Type these in the game's chat. Arguments are separated by spaces.

| Command | Meaning |
|---|---|
| `/help` | List the commands |
| `/pos` | Show your scene and position |
| `/tele x y z` | Teleport within the current scene |
| `/scene id [x y z]` | Go to a scene (defaults to its wayshrine) |
| `/wayshrine id`, `/wayshrines` | Travel to a wayshrine / list them |
| `/time 0-2400` | Set the time of day |
| `/give itemId [amount]` | Give yourself an item |
| `/silver n` | Set your silver |
| `/quest id [phase]` | Show or set a quest phase |
| `/save` | Save now (autosave runs every 60 s and on logout) |

The client sends `/useportal` itself when you confirm the snowy portal.

## Plugin settings (`<game>\BepInEx\config\gso.offline.server.cfg`)

| Section.Key | Default | Meaning |
|---|---|---|
| World.NpcViewDistance | 120 | NPC streaming radius (m) |
| World.HarvestableViewDistance | 90 | Harvestable streaming radius (m) |
| World.UnlockAllWayshrines | true | All wayshrines unlocked |
| World.StartTimeOfDay | 1000 | Time sent on scene load (negative = client default) |
| Convenience.AutoLogin / AutoCharacter | empty | Skip the menus |
| Debug.LogUnhandledEvents | true | Log unimplemented client events |
| Debug.DevCommandFile | empty | Enables the DevBridge |
