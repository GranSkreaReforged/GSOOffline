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

Step-by-step guides for installing from a release zip or from your own build, updating, uninstalling and troubleshooting are in [docs/INSTALL.md](docs/INSTALL.md). The short version:

1. Download `GSOOffline-<version>-with-BepInEx.zip` from the releases page.
   - If you already have BepInEx 5 x64 installed, use `GSOOffline-<version>.zip` instead.
2. Extract it into the game folder, next to `GSO.exe`. (In Steam: right-click Gran Skrea Online → Manage → Browse local files.)
3. Start the game, then log in with any username. The password is ignored.

Characters are saved in `OfflineSaves/` inside the game folder. Settings are in `BepInEx/config/gso.offline.server.cfg`; `AutoLogin` and `AutoCharacter` skip the menus.

The mod's version is shown in the top-left corner. At startup it asks GitHub for the latest release (only the version number is read) and adds "NEW UPDATE AVAILABLE" if there's a newer one; download it from the [releases page](https://github.com/GranSkreaReforged/GSOOffline/releases). `Updates.ShowVersion` and `Updates.CheckForUpdates` in the config turn these off.

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

Work happens on `feature/<area>/<name>` branches from `dev`, merged back into `dev`; risky or large changes go through a reviewed pull request. `main` only receives releases, each one a pull request from `dev`. `build.ps1` makes dev builds (versioned like `1.0.0-dev+<branch>.<commit>`); only `release.ps1` makes release builds. The full flow is in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md#6-branches-and-releases).

```powershell
.\release.ps1 -Version 0.2.0 -DryRun   # on dev: build and package into dist\0.2.0\ only
.\release.ps1 -Version 0.2.0           # also bump the version, date the CHANGELOG, commit the release notes, and tag v0.2.0
git push origin dev                    # then open the dev -> main pull request; push the tag once it's merged
```

Each release gets its own folder, `dist\<version>\`, with the plugin-only zip, the zip with BepInEx included, `SHA256SUMS.txt`, and the release notes (`RELEASE_NOTES-<version>.md`, written by hand). BepInEx is pinned to a specific version and SHA-256 in `build\GSOBuild.psm1`. The version number lives only in `Directory.Build.props`, and the plugin's `[BepInPlugin]` version is generated from it.

There is no CI build, because compiling needs the proprietary game assemblies.

### Developer tools (separate repo)

Decompiling, data mining (including the door table generator) and the DevBridge, which drives the real client by script for testing, live in the sibling **GSODevTools** repo (`..\GSODevTools`). See its README. This repo keeps its own bridge commands in `src/GSOOffline/DevCommands.cs` (listed in [docs/COMMANDS.md](docs/COMMANDS.md)), which GSODevTools discovers at runtime with no compile-time reference.

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
  OfflineServer.Boats.cs       boats and sailing
  OfflineServer.Ferries.cs     ferries (transports) and zone crossings
  OfflineServer.Oysters.cs     Bal Sardan oyster fishing
  FerryRoutePlanner.cs         dev tool: water maps and water-only routes (routeplan, watermap)
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
- Shops, with the original game's stock lists, limited stock and restocking
- Harvesting (mining, woodcutting, fishing, gathering)
- Sailing: launching, boarding and steering boats, Sailing XP, boat teleports
- Oyster fishing in Bal Sardan: harpoon guns and the fishing vessel's crane
- Ferries: the lighthouse-Yorkhill ship and the lighthouse-Bal Sardan crossing
- Cart riders (15 silver to the Monastery, Lighthouse, Grimwall or the Farm) and boat rides (Bal Sardan north-south, the Monastery to the Coral beach)
- Item grades (damaged to mythic): new gear rolls its grade, a grade scales the gear's stats and a weapon's hits, and gear upgrades by using another of the same item, an upgrade stone or a grade stone on it
- Crafting at workbenches
- Doors, dungeon entrances and interiors
- Bank
- Melee/ranged basic attacks, NPC aggro and chasing, death and respawn
- Abilities: attacks, area attacks, stuns and roots, damage over time, heals, bandages and buffs with their icons (effect numbers are reconstructions)
- Light and heavy armor XP, shared from kills made shortly after being hit
- Potions, food, experience scrolls, teleport scrolls and gear crates
- Loot bags: kills drop a bag with the monster's loot; click it to take everything
- Particles and sounds: arrows, bolts and spells fly and land; ranged NPCs shoot back; ability, hit, death, harvesting, tree-felling, level-up and teleport effects and sounds
- NPCs and harvestables placed in the world
- Client-side interactables such as workbenches
- Chat
- Aellope, Goldon's flying world boss: patrols one of five routes over the island, dives when shot, fights on the ground, takes off again after 20 seconds without damage, feeds on a fallen player, and returns an hour after she's killed (the original server's behaviour)
- Dungeon mechanics from the original server: Lakhmu's basement (lock puzzle, falling rocks), Ulan's dungeon (gate and key, lift, levers), the Poacher dungeon (vault, storage room and dynamite, lava room, traps, chests), the Yorkhill sewers' bridge, the Coral dungeon (water, tidal waves, explosions)
- Boss mechanics from the original server: Nirish (webs, poison clouds, his healing spiderling), George (dynamite), Sytrax (flight and fire) and Sepitus (bolts, plague, healing bird, lava hounds)
- Reconstructed boss mechanics: fog fields, water and psychic explosions, stuns, ground slams and summoned minions (the Supreme Sea Serpent, Zalranaz, the Tomb guardians, Lydia, Jotnar), and Absorb (Zalranaz, the Tomb guardians), which reflects your hits for a few seconds; timings and damage are reconstructions
- Pets: the pet in the Pet slot walks with you and fights what you fight; the parrot and the bats ride on your shoulder (and the parrot talks with `/parrotsay`)

Partially working:
- Quests. Playable from start to finish: An Honest Day's Work, Flowerful Persuasions, A Deadly Investigation, Into the Depths, In the Eyes of a Child, A Life Experience, An Unfinished Affair, The Abandoned Mine, The Lone Hunter, Growing Pains, A Rat's Tail, Rum-Run, A Cook Book, No True Huntsman, Sightseeing, Lakhmu's basement, A Fishy Request, Powder Monkey, Mantle of St. Jakob, A Magical Journey, Commander Grant's Gloves and Joining the Hunters Guild. Each quest NPC appears only in its own phases, ambushes and bosses spawn when the story calls for them, and the quest steps (places, objects, items, kills, chat) are rebuilt in `src/GSOOffline/Data/content.json`. Left out: The Breaching Light and Wand of Ke'yars Eketosh, hidden in the game data and never finished by the original server.

Not yet implemented:
- Movement speed buffs and resistance/stamina potions (the icon shows, but the effect isn't modelled)
- Effects for abilities the data doesn't link to an effect or projectile (most are mapped by name; see `OfflineServer.Effects.cs`)
- Building doors pick one shared spot in each interior scene, because the per-door room mapping is lost

Loot comes from the original server's drop tables (`Loot.xml`, sent by the game's developer, from about update 136), turned into `src/GSOOffline/Data/droptables.json` by GSODevTools' `gen_droptables.py` and rolled as the original rolled them: each drop on its own, 1 in its rate. Every creature can also drop an upgrade or grade stone and a gear crate for its level, by the original's formulas. Creatures added after that snapshot (Bruno, Betsy, Lakhar, Lydia, Jotnar, Argus, the Elder Ent...) and the boss uniques and pets of updates 137-140 come from the [Gran Skrea Online community wiki](https://gran-skrea-online.fandom.com) (CC BY-SA) and the official patch notes on Steam, in `src/GSOOffline/Data/loot.json` (`gen_loot.py`); the wiki names how rare drops were but not the rates, so those are reconstructed chances, and such a creature without a wiki entry drops silver by level.

Shop stock is a reconstruction, because the original lists were lost with the server. Shopkeepers were identified from the dialogue trees, and each shop sells thematically matching items at their listed prices.

Unimplemented client events are logged to `BepInEx/LogOutput.log` as `Unhandled client event X/Y`.

## License

The code in this repository is licensed under the **GNU General Public License, version 3 or (at your option) any later version** (GPL-3.0-or-later); see [LICENSE](LICENSE).

The license covers only this project's own source code and tooling. It does not cover Gran Skrea Online or any of its files, assets or data, which remain the property of their respective owners and are not included here. BepInEx (LGPL-2.1) and HarmonyX (MIT) are separate projects under their own licenses.
