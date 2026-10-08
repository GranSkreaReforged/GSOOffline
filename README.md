# GSO Offline Server

A BepInEx plugin that lets you play **Gran Skrea Online** after the official servers shut down. It runs a small replacement game server inside the game process. No game files are modified or redistributed, so you need your own copy of the game.

## About this project

Gran Skrea Online is **no longer sold on the Steam store**, and its official servers have been shut down. Without a server, copies of the game that people already own can't be played.

This is a **non-commercial passion project**. It exists to keep a game I loved playable for people who already own it.

- It is not affiliated with or endorsed by the original developers or publisher.
- It is free, and there is no intention to make money from it in any form: no sales, no donations tied to it, no paid features.
- It does not include, sell or redistribute any game content. You must already own the game.
- "Gran Skrea Online" and all game assets belong to their respective owners.

## Playing (release zip)

1. Download `GSOOffline-<version>-with-BepInEx.zip` from the releases page.
   - If you already have BepInEx 5 x64 installed, use `GSOOffline-<version>.zip` instead.
2. Extract it into the game folder, next to `GSO.exe`. (In Steam: right-click Gran Skrea Online → Manage → Browse local files.)
3. Start the game, then log in with any username. The password is ignored.

Characters are saved in `OfflineSaves/` inside the game folder. Settings are in `BepInEx/config/gso.offline.server.cfg`; `AutoLogin` and `AutoCharacter` skip the menus.

In-game chat commands: `/help`, `/pos`, `/tele x y z`, `/scene id [x y z]`, `/wayshrine id`, `/wayshrines`, `/time 0-2400`, `/save`.

To uninstall, delete `winhttp.dll`, `doorstop_config.ini`, `BepInEx/` and `steam_appid.txt` from the game folder.

## Building it yourself

New to the project? Start with [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md). Every script, test command, chat command and setting is listed in [docs/COMMANDS.md](docs/COMMANDS.md).

Requirements:
- Windows, the [.NET SDK](https://dotnet.microsoft.com/download) 9.0.200 or newer (for the `.slnx` solution)
- Your own installed copy of Gran Skrea Online. The build reads the game's DLLs to compile against; it never copies them into the output and never writes to the game.
- PowerShell 7 for releases. Building also works in Windows PowerShell 5.1.

```powershell
.\build.ps1                          # Debug build into artifacts\build\Debug; the game is untouched
.\build.ps1 -Deploy                  # ...and copy it into the game (close the game first)
.\build.ps1 -InstallBepInEx -Deploy  # first time, as a convenience: also put BepInEx + steam_appid.txt in the game
.\build.ps1 -Configuration Release
```

Every build lands in `artifacts\build\<Configuration>\`, laid out exactly like the game folder, with an `INSTALL.txt`:

```
artifacts\build\Debug\
  INSTALL.txt
  BepInEx\plugins\GSOOffline\GSOOffline.dll
```

Review it there, then install it either way:
- **By hand:** install [BepInEx 5.4.23.5 (x64)](https://github.com/BepInEx/BepInEx/releases) into the game folder (the one with `GSO.exe`), then copy this `BepInEx` folder into it, merging. Optionally add `steam_appid.txt` containing `595110` to start `GSO.exe` without Steam.
- **With the script:** `-Deploy` does the copy; `-InstallBepInEx` does the BepInEx and `steam_appid.txt` part.

BepInEx's DLLs for compiling come from the same pinned, hash-checked BepInEx zip, unpacked into `.cache\`, so you don't need BepInEx in the game to build.

The game folder is found automatically by searching every Steam library. You can override it in any of these ways:
- `-GameDir <path>` (remembered in the git-ignored `GameDir.user.props`)
- the `GSO_GAME_DIR` environment variable
- `dotnet build -p:GameDir=<path>`

### Releasing

```powershell
.\release.ps1 -Version 0.2.0 -DryRun   # build and package into dist\ only
.\release.ps1 -Version 0.2.0           # also bump the version, date the CHANGELOG, commit, and tag v0.2.0
git push --follow-tags
```

`dist\` receives the plugin-only zip, the zip with BepInEx included, and `SHA256SUMS.txt`. BepInEx is pinned to a specific version and SHA-256 in `build\GSOBuild.psm1`. The version number lives only in `Directory.Build.props`, and the plugin's `[BepInPlugin]` version is generated from it.

There is no CI build, because compiling needs the proprietary game assemblies.

### Developer tools (separate repo)

Decompiling, data mining (including the door table generator) and the DevBridge, which drives the real client by script for testing, live in the sibling **GSODevTools** repo (`..\GSODevTools`). See its README. This repo keeps one bridge hook: `src/GSOOffline/DevCommands.cs` (`checkrecipes`), which GSODevTools discovers at runtime with no compile-time reference.

### Working with Claude Code

`CLAUDE.md` and `.claude/skills/` describe this repo's workflows (testing, decompiling and data-mining skills are in GSODevTools):

| Skill | Covers |
|---|---|
| `gso-build` | build.ps1, net35 constraints, deploy |
| `gso-release` | branches, merging, release.ps1 |
| `gso-content` | quest glue in content.json |
| `gso-server-feature` | end-to-end recipe for adding a server system |

## How it works

See [docs/PROTOCOL.md](docs/PROTOCOL.md) for the message map. In short:
- The client is a thin client. The old Photon server ran everything.
- Everything the client sends goes through `Scr_RPCSender.RaiseEvent`. A Harmony prefix hands it to `OfflineServer.Receive`.
- Replies are queued and fed into `Scr_RPCReceiver.OnEventCall`, exactly as Photon would have delivered them.
- The Photon connect loop, the server-status checks and the heartbeat timeout are patched out.
- World content comes from data the client already ships:
  - `Resources/XMLs/*`: NPC stats, dialogue, quests, abilities, harvestables, wayshrines and more.
  - `Scr_NPCDummy` and `Scr_HarvestableDummy` placement markers left in every scene by the developers' export tool.

```
src/GSOOffline/
  Plugin.cs                    BepInEx entry, config
  Patches.cs                   RaiseEvent hook, fake Photon connection
  SteamPatches.cs              Steamworks calls made safe without Steam
  OfflineServer.cs             event queue, dispatch, delivery
  OfflineServer.Account.cs     login, characters, enter world
  OfflineServer.World.cs       scenes, teleports, wayshrines, NPC/harvestable streaming
  OfflineServer.Chat.cs        chat and commands
  GameData.cs                  XML data loaders
  World.cs                     per-scene entities built from scene markers
  SaveSystem.cs                JSON saves
```

## Status

Working:
- Login and characters
- Entering the world and zone loading
- Saving
- Wayshrine travel
- Inventory, equipment and starter kits
- NPC dialogue
- Quest progress and rewards
- Shops
- Harvesting (mining, woodcutting, fishing, gathering)
- Crafting at workbenches
- Doors, dungeon entrances and interiors
- Bank
- Melee/ranged basic attacks, NPC aggro and chasing, death and respawn
- NPCs and harvestables placed in the world
- Client-side interactables such as workbenches
- Chat

Partially working:
- Quests. Conversations that progress through dialogue work, and the start of the tutorial quest plays through. Steps that depend on harvesting, crafting or combat wait on those systems. NPC/conversation links for older quests are being added to `src/GSOOffline/Data/content.json`.

Not yet implemented:
- NPC loot tables (kills drop silver), projectile/spell visuals for abilities, buffs from support abilities
- Building doors pick one shared spot in each interior scene, because the per-door room mapping is lost
- Showing only the right copy of a quest NPC (each appears in several places)

Shop stock is a reconstruction, because the original lists were lost with the server. Shopkeepers were identified from the dialogue trees, and each shop sells thematically matching items at their listed prices.

Unimplemented client events are logged to `BepInEx/LogOutput.log` as `Unhandled client event X/Y`.

## License

The code in this repository is licensed under the **GNU General Public License, version 3 or (at your option) any later version** (GPL-3.0-or-later); see [LICENSE](LICENSE).

The license covers only this project's own source code and tooling. It does not cover Gran Skrea Online or any of its files, assets or data, which remain the property of their respective owners and are not included here. BepInEx (LGPL-2.1) and HarmonyX (MIT) are separate projects under their own licenses.
