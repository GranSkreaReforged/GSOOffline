# Changelog

All notable changes are documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
`release.ps1` turns the Unreleased heading into a version heading.

## [Unreleased]
### Fixed
- Stack counts show again in the inventory and bank (e.g. "12" on a stack of logs). The windows only draw a count for items with id 0, which is how the original server sent stackable items; stacks now go to the client that way.
- Sorting the inventory no longer scrambles it on the server. The client sorts its own list and sends only "sorted"; dragging or swapping items afterwards moved the wrong ones, and the order came back wrong after a relog. The server now adopts the client's order.
- Steam no longer shows the game as running after you quit, with "Stop" stuck on "Stopping", when you opened an in-game link (Leaderboards, Discord, wiki...) while your browser was closed. The game started the browser as its own child process, with the Steam overlay injected, so Steam waited for the browser to close. Links now open through the Windows shell, outside the game's processes.

### Added
- `openurl <url>` bridge command: calls `Application.OpenURL` as a menu link would.
- `invorder` and `sortinv` bridge commands: compare the server's and the client's inventory and bank row by row, and sort as the inventory's "By name" button does.

### Changed
- Branching: work happens on `feature/<area>/<name>` branches merged into `dev`, and each release is one merge of `dev` into `main` (`docs/DEVELOPMENT.md`).

## [1.0.0] - 2026-10-08
### Changed
- The DevBridge, `devbridge.ps1`, `decompile.ps1` and the datamining tools moved to the separate GSODevTools repo. The DevBridge is now its own plugin, and the `Debug.DevCommandFile` setting is gone. `checkrecipes` remains as a GSOOffline bridge command (`DevCommands.cs`), now joined by `npcbounds`, `animclips`, `door`, `clearspot`, `probe` and `playerstate`.
- Builds no longer touch the game. `build.ps1` puts the plugin in `artifacts\build\<Configuration>\`, laid out like the game folder, with an `INSTALL.txt` saying where it goes; `-Deploy` (replacing the old default and `-NoDeploy`) copies it into the game. BepInEx's DLLs for compiling come from the pinned BepInEx zip, so building doesn't need BepInEx installed in the game.
- `docs/INSTALL.md`: installation guides from a release and from a build (with updating, uninstalling and troubleshooting), also shipped in the release zips.

### Added
- Inventory and items:
  - Items are saved per character, and their stats are rolled from the item database.
  - Equip, unequip, destroy, drop, swap/insert and item tabs work.
  - Using food heals.
- Starter kits from the character creator: clothes, a weapon for the chosen fighting style, profession items, and level 5 in the profession skill.
- NPC dialogue:
  - Conversations follow the game's own dialogue trees, including quest, item and quest-variable conditions on options.
  - Which conversation an NPC opens with is rebuilt from the dialogue graph, plus `Data/content.json`.
- Quests:
  - Phases and the quest tracker update as you progress.
  - Completing a quest grants its item, silver and XP rewards.
  - Quests advance on item-based triggers.
  - The first steps of "An Honest Day's Work" are playable through Merrick's hand-in.
- Shops:
  - All 47 shop ids are stocked with rebuilt inventories that fit their merchant.
  - Buying and selling use the client's own pricing rules, and merchants have silver.
- Harvesting:
  - Mining, woodcutting, fishing and gathering use the game's own drop tables, and drop chances rise with skill level.
  - Each node has a tool and level requirement and a limited amount before it depletes, then respawns.
  - Gathering repeats until the node is used up or you walk away.
- Crafting:
  - All 341 recipes are rebuilt with the same ids as the client.
  - Crafting checks materials and skill level, takes the recipe time, and supports batch crafting.
- Harvesting and crafting give XP from a level-scaled base. The original formula was lost; it lives in `SkillData.BaseXp`.
- Combat:
  - Targeting and auto-attack with weapon-based range and speed. Damage scales with the weapon's damage stats and combat skill, and the skill matching the weapon gets XP.
  - NPCs aggro, chase, swing at their own attack speed, leash home and respawn.
  - Kills drop silver and advance quest kill objectives.
  - When you die you respawn at your home wayshrine, with brief protection from aggro.
  - Bandage-heal abilities work, and ability bar slots are saved.
  - Combat numbers live in `CombatRules`, since the originals were lost.
  - Town guards no longer attack on sight (offline, there are no criminals).
- `tools/dev/devbridge.ps1` and `docs/DEVBRIDGE.md`: enable/disable the DevBridge, launch, send commands, read results.
- Abilities on the action bar work:
  - Level, mana, range and cooldown come from the game's ability data.
  - Targeted abilities hit the target, and sweeping weapon techniques hit everything in reach. Damage is a multiple of a basic hit, because the original ability damage values were lost.
- Mana, plus health and mana regeneration.
- NPCs wander around their spawn points using the game's own wander settings, kept on the ground with terrain raycasts.
- Doors and zone transitions:
  - Dungeon, cave and monastery entrances and exits are paired from the developers' own object names (`tools/datamining/gen_doors.py` builds `Data/doors.json`).
  - Building doors lead into their town's interior scene, and exits return you to the door you used.
  - The snowy portal works.
  - Generic doors report as locked.
- Bank: deposit and withdraw items (unique items keep their rolled stats) and silver, from bank chests or a banker's dialogue.
- `content.json` overrides placed next to the DLL replace the built-in quest content without rebuilding.
- Chat commands `/give`, `/silver` and `/quest`.
- An opt-in developer automation bridge (`Debug.DevCommandFile`) for testing through the real client.

### Fixed
- Your character no longer stays stuck in the attack animation after killing a creature. Every swing puts the client into attack mode and the server never told it the fight was over; it now does so when the target dies, when you cancel, when the target is cleared, on death and on scene changes.
- Doors no longer drop you behind rocks or walls. Arrival points were guessed offline and some landed in a pocket you couldn't walk out of (leaving the Grimwall mine, leaving Roke's dungeon by the ruins). The server now picks the arrival in-game: solid, flat-enough ground with room to stand, in sight of the door, preferring open space. Scenes whose floors appear a moment after loading (Roke's dungeon) keep the loading screen up until the floor exists, instead of letting you fall through. All 18 doors were checked in-game.
- NPCs are animated again. The server sent animation 0 (an empty clip) for every NPC, so people stood frozen and sunk to the waist, and animals stretched hundreds of metres across the map. NPCs now get their idle, run, attack and death animations from the game data.
- Saves now keep inventories, skills and other lists. UnityEngine.JsonUtility had silently dropped them, so it was replaced with the plugin's own JSON serializer.
- Max health and carrying capacity are now sent at login.
- Equipment slots are restored correctly after relogging.

### Added (0.1 groundwork)
- Offline server that replaces the shut-down Photon game server, running inside the game process.
- Login with any account name. Accounts and characters are saved as JSON in `OfflineSaves/`.
- Character list, creation (including the appearance editor) and deletion.
- Entering the world, scene loading, position saving and autosave.
- Wayshrine travel and discovery, and setting a home wayshrine.
- NPC and harvestable spawns rebuilt from the developers' scene markers and streamed by distance.
- Chat and offline commands (`/help`, `/pos`, `/tele`, `/scene`, `/wayshrine`, `/wayshrines`, `/time`, `/save`).
- Steamworks calls made safe so the game still works when it is launched without Steam.
- Build, release and data-extraction tooling.
- GPL-3.0-or-later license (included in release zips) and a README note that this is a non-commercial project for a game no longer sold on Steam.
