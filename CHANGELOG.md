# Changelog

All notable changes are documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
`release.ps1` turns the Unreleased heading into a version heading.

## [Unreleased]

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
- Claude Code project skills in `.claude/skills/` for build, testing, release, decompiling, data mining, quest content, and adding server features.
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
